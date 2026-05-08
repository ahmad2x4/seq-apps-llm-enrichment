using System;
using System.Collections.Generic;
using Seq.Apps;
using Seq.Apps.LogEvents;
using Serilog;
using Serilog.Events;
using SerilogLevel = Serilog.Events.LogEventLevel;
using SeqLevel = Seq.Apps.LogEvents.LogEventLevel;

namespace Seq.Apps.LlmEnrichment.Tests.Support
{
    static class Some
    {
        public const uint AlertV1 = 0xA1E77000;
        public const uint AlertV2 = 0xA1E77001;
        public const uint NonAlertEventType = 0x12345678;

        public static Event<LogEvent> AlertLogEvent(
            string messageTemplate = "Alert condition triggered",
            uint eventType = AlertV1,
            SerilogLevel level = SerilogLevel.Error,
            Dictionary<string, LogEventPropertyValue>? extraProperties = null)
        {
            var log = new LoggerConfiguration().CreateLogger();
            log.BindMessageTemplate(messageTemplate, Array.Empty<object>(), out var template, out var properties);

            var allProperties = new List<LogEventProperty>(properties ?? Array.Empty<LogEventProperty>());
            if (extraProperties != null)
                foreach (var (k, v) in extraProperties)
                    allProperties.Add(new LogEventProperty(k, v));

            var logEvent = new LogEvent(DateTimeOffset.UtcNow, level, null, template!, allProperties);
            return new Event<LogEvent>("evt-1", eventType, DateTime.UtcNow, logEvent);
        }

        public static Event<LogEventData> AlertLogEventData(
            string renderedMessage = "Alert condition triggered",
            uint eventType = AlertV1,
            Dictionary<string, object?>? extraProperties = null)
        {
            var props = new Dictionary<string, object?>(extraProperties ?? new Dictionary<string, object?>());
            props.TryAdd("Alert.Title", "Test Alert");

            var data = new LogEventData
            {
                Id = "evt-1",
                LocalTimestamp = DateTimeOffset.UtcNow,
                Level = SeqLevel.Error,
                MessageTemplate = renderedMessage,
                RenderedMessage = renderedMessage,
                Properties = props
            };
            return new Event<LogEventData>("evt-1", eventType, DateTime.UtcNow, data);
        }
    }
}
