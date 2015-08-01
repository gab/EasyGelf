﻿using System;
using System.Globalization;
using EasyGelf.Core;
using EasyGelf.Core.Transports;
using NLog;
using NLog.Targets;
using NLog.Config;
using NLog.Layouts;
using System.Collections.Generic;

namespace EasyGelf.NLog
{
    public abstract class GelfTargetBase : TargetWithLayout
    {
        private ITransport transport;
        private IEasyGelfLogger logger;

        public string Facility { get; set; }

        public string HostName { get; set; }

        public bool IncludeSource { get; set; }

        public bool IncludeEventProperties { get; set; }

        public bool UseRetry { get; set; }

        public int RetryCount { get; set; }

        public TimeSpan RetryDelay { get; set; }

        public bool IncludeStackTrace { get; set; }

        public bool Verbose { get; set; }

        [ArrayParameter(typeof(GelfParameterInfo), "parameter")]
        public IList<GelfParameterInfo> Parameters { get; private set; }

        protected GelfTargetBase()
        {
            Facility = "gelf";
            HostName = Environment.MachineName;
            IncludeSource = true;
            IncludeEventProperties = true;
            UseRetry = true;
            RetryCount = 5;
            RetryDelay = TimeSpan.FromMilliseconds(50);
            IncludeStackTrace = true;
            Verbose = false;

            Parameters = new List<GelfParameterInfo>();
        }

        protected abstract ITransport InitializeTransport(IEasyGelfLogger logger);

        protected override void Write(LogEventInfo loggingEvent)
        {
            try
            {
                var renderedEvent = Layout.Render(loggingEvent);
                var messageBuilder = new GelfMessageBuilder(renderedEvent, HostName, loggingEvent.TimeStamp, ToGelf(loggingEvent.Level))
                    .SetAdditionalField(GelfAdditionalFields.Facility, Facility)
                    .SetAdditionalField(GelfAdditionalFields.LoggerName, loggingEvent.LoggerName);
                if (IncludeSource)
                {
                    var userStackFrame = loggingEvent.UserStackFrame;
                    if (userStackFrame != null)
                    {
                        messageBuilder.SetAdditionalField(GelfAdditionalFields.SourceFileName, userStackFrame.GetFileName());
                        messageBuilder.SetAdditionalField(GelfAdditionalFields.SourceLineNumber, userStackFrame.GetFileLineNumber().ToString(CultureInfo.InvariantCulture));
                    }
                }
                if (IncludeStackTrace)
                {
                    var exception = loggingEvent.Exception;
                    if (exception != null)
                    {
                        messageBuilder.SetAdditionalField(GelfAdditionalFields.ExceptionMessage, exception.Message);
                        messageBuilder.SetAdditionalField(GelfAdditionalFields.ExceptionStackTrace, exception.StackTrace);
                    }
                }

                //Add user-defined fields from config
                foreach (GelfParameterInfo param in Parameters)
                {
                    var value = param.Layout.Render(loggingEvent);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    var key = param.Name;

                    long longValue;
                    decimal decimalValue;
                    bool boolValue;

                    if (param.Type == GelfParameterInfoType.Boolean
                        && BooleanTryParse(value, out boolValue))
                    {
                        messageBuilder.SetAdditionalField(key, boolValue);
                    }
                    else if (param.Type == GelfParameterInfoType.Integer
                        && long.TryParse(value, out longValue))
                    {
                        messageBuilder.SetAdditionalField(key, longValue);
                    }
                    else if (param.Type == GelfParameterInfoType.Decimal
                        && decimal.TryParse(value, out decimalValue))
                    {
                        messageBuilder.SetAdditionalField(key, decimalValue);
                    }
                    else
                    {
                        messageBuilder.SetAdditionalField(key, value);
                    }
                }

                //Add log event properties
                if (IncludeEventProperties)
                {
                    foreach (var property in loggingEvent.Properties)
                    {
                        messageBuilder.SetAdditionalField(property.Key.ToString(), property.Value.ToString());
                    }
                }

                transport.Send(messageBuilder.ToMessage());
            }
            catch (Exception exception)
            {
                logger.Error("Failed to send message", exception);
            }
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            logger = Verbose ? (IEasyGelfLogger)new VerboseLogger() : new SilentLogger();
            var mainTransport = InitializeTransport(logger);
            transport = new BufferedTransport(logger, UseRetry ? new RetryingTransport(logger, mainTransport, RetryCount, RetryDelay) : mainTransport);
        }

        protected override void CloseTarget()
        {
            base.CloseTarget();
            if (transport == null)
                return;
            transport.Close();
            transport = null;
        }


        private static GelfLevel ToGelf(LogLevel level)
        {
            if (level == LogLevel.Debug)
                return GelfLevel.Debug;
            if (level == LogLevel.Fatal)
                return GelfLevel.Critical;
            if (level == LogLevel.Info)
                return GelfLevel.Informational;
            if (level == LogLevel.Trace)
                return GelfLevel.Informational;
            return level == LogLevel.Warn ? GelfLevel.Warning : GelfLevel.Error;
        }

        private static bool BooleanTryParse(string value, out bool boolValue)
        {
            if (value == "0" || value.Equals(bool.FalseString, StringComparison.InvariantCultureIgnoreCase))
            {
                boolValue = false;
                return true;
            }
            if (value == "1" || value.Equals(bool.TrueString, StringComparison.InvariantCultureIgnoreCase))
            {
                boolValue = true;
                return true;
            }
            boolValue = false;
            return false;
        }

        [NLogConfigurationItem]
        public class GelfParameterInfo
        {
            [RequiredParameter]
            public string Name { get; set; }

            [RequiredParameter]
            public Layout Layout { get; set; }

            /// <summary>
            /// Convert the value to the specified type: String (default), Integer, Decimal.
            /// </summary>
            public string LayoutType
            {
                get { return Type.ToString(); }
                set
                {
                    switch (value.ToLowerInvariant())
                    {
                        case "bool":
                        case "boolean":
                            Type = GelfParameterInfoType.Boolean;
                            break;
                        case "int":
                        case "integer":
                            Type = GelfParameterInfoType.Integer;
                            break;
                        case "numeric":
                        case "decimal":
                            Type = GelfParameterInfoType.Decimal;
                            break;
                        default:
                            Type = GelfParameterInfoType.String;
                            break;
                    }
                }
            }

            public GelfParameterInfoType Type { get; private set; }
        }

        public enum GelfParameterInfoType
        {
            String,
            Boolean,
            Integer,
            Decimal
        }
    }
}
