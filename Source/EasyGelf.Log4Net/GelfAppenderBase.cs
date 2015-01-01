﻿using System;
using EasyGelf.Core;
using JetBrains.Annotations;
using log4net.Appender;
using log4net.Core;

namespace EasyGelf.Log4Net
{
    public abstract class GelfAppenderBase  : AppenderSkeleton
    {
        private ITransport transport;

        [UsedImplicitly]
        public string Facility { get; set; }

        [UsedImplicitly]
        public bool IncludeSource { get; set; }

        [UsedImplicitly]
        public string Host { get; set; }

        protected GelfAppenderBase()
        {
            Facility = "gelf";
            IncludeSource = true;
            Host = Environment.MachineName;
        }

        public override void ActivateOptions()
        {
            base.ActivateOptions();
            try
            {
                transport = InitializeTransport();
            }
            catch (Exception exception)
            {
                ErrorHandler.Error("Failed to create UdpTransport", exception);
                throw;
            }
        }

        protected override bool RequiresLayout
        {
            get { return true; }
        }

        protected abstract ITransport InitializeTransport();

        protected override void Append(LoggingEvent loggingEvent)
        {
            try
            {
                var renderedEvent = RenderLoggingEvent(loggingEvent);
                var messageBuilder = new GelfMessageBuilder(renderedEvent, Host)
                    .SetLevel(loggingEvent.Level.ToGelf())
                    .SetTimestamp(loggingEvent.TimeStamp)
                    .SetAdditionalField("facility", Facility)
                    .SetAdditionalField("loggerName", loggingEvent.LoggerName)
                    .SetAdditionalField("threadName", loggingEvent.ThreadName);
                if (IncludeSource)
                {
                    var locationInformation = loggingEvent.LocationInformation;
                    if (locationInformation != null)
                    {
                        messageBuilder.SetAdditionalField("sourceFileName", locationInformation.FileName)
                            .SetAdditionalField("sourceClassName", locationInformation.ClassName)
                            .SetAdditionalField("sourceMethodName", locationInformation.MethodName)
                            .SetAdditionalField("sourceLineNumber", locationInformation.LineNumber);
                    }
                }
                transport.Send(messageBuilder.ToMessage());
            }
            catch (Exception exception)
            {
                ErrorHandler.Error("Unable to send logging event to remote host", exception, ErrorCode.WriteFailure);
            }
        }

        protected override void OnClose()
        {
            base.OnClose();
            if (transport == null)
                return;
            CoreExtentions.SafeDo(transport.Close);
            transport = null;
        }

    }
}