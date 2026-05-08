using System;
using System.Threading.Tasks;
using Seq.Apps;
using Seq.Apps.LogEvents;
using Serilog.Events;

namespace Seq.Apps.LlmEnrichment.Tests.Support
{
    // Concrete testable subclass for LogEvent (HTTP app flavour)
    class TestLogEventApp : LlmEnrichedSeqApp<LogEvent>, ISubscribeToAsync<LogEvent>
    {
        public string SummaryToReturn { get; set; } = "Test summary";
        public Exception? ExceptionToThrow { get; set; }
        public string? LastPrompt { get; private set; }

        protected override Task<string> CallLlmAsync(Event<LogEvent> evt)
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            return Task.FromResult(SummaryToReturn);
        }

        // Expose EnrichAsync for direct testing
        public Task<Event<LogEvent>> EnrichEventAsync(Event<LogEvent> evt) => EnrichAsync(evt);

        public Task OnAsync(Event<LogEvent> evt) => Task.CompletedTask;
    }

    // Concrete testable subclass for LogEventData (Teams/Slack app flavour)
    class TestLogEventDataApp : LlmEnrichedSeqApp<LogEventData>, ISubscribeToAsync<LogEventData>
    {
        public string SummaryToReturn { get; set; } = "Test summary";
        public Exception? ExceptionToThrow { get; set; }

        protected override Task<string> CallLlmAsync(Event<LogEventData> evt)
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            return Task.FromResult(SummaryToReturn);
        }

        public Task<Event<LogEventData>> EnrichEventAsync(Event<LogEventData> evt) => EnrichAsync(evt);

        public Task OnAsync(Event<LogEventData> evt) => Task.CompletedTask;
    }
}
