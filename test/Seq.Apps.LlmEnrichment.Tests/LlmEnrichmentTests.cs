using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Seq.Apps.LlmEnrichment.Tests.Support;
using Serilog.Events;
using Xunit;

namespace Seq.Apps.LlmEnrichment.Tests
{
    public class LlmEnrichmentTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        static TestLogEventApp AttachedLogEventApp(string? apiKey = "test-key", string? model = "gpt-4o-mini")
        {
            var app = new TestLogEventApp { LlmApiKey = apiKey, LlmModel = model };
            app.Attach(new TestAppHost());
            return app;
        }

        static TestLogEventDataApp AttachedLogEventDataApp(string? apiKey = "test-key", string? model = "gpt-4o-mini")
        {
            var app = new TestLogEventDataApp { LlmApiKey = apiKey, LlmModel = model };
            app.Attach(new TestAppHost());
            return app;
        }

        // ── Guard: LLM not configured ─────────────────────────────────────────

        [Fact]
        public async Task EnrichAsync_WhenApiKeyBlank_ReturnsEventUnchanged()
        {
            var app = AttachedLogEventApp(apiKey: "");
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.Same(evt, result);
            Assert.False(result.Data.Properties.ContainsKey("AISummary"));
        }

        [Fact]
        public async Task EnrichAsync_WhenModelBlank_ReturnsEventUnchanged()
        {
            var app = AttachedLogEventApp(model: "");
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.Same(evt, result);
            Assert.False(result.Data.Properties.ContainsKey("AISummary"));
        }

        // ── Guard: event type ─────────────────────────────────────────────────

        [Fact]
        public async Task EnrichAsync_WhenNonAlertEventType_ReturnsEventUnchanged()
        {
            var app = AttachedLogEventApp();
            var evt = Some.AlertLogEvent(eventType: Some.NonAlertEventType);

            var result = await app.EnrichEventAsync(evt);

            Assert.Same(evt, result);
            Assert.False(result.Data.Properties.ContainsKey("AISummary"));
        }

        [Fact]
        public async Task EnrichAsync_WhenAlertV1EventType_CallsLlm()
        {
            var app = AttachedLogEventApp();
            app.SummaryToReturn = "V1 summary";
            var evt = Some.AlertLogEvent(eventType: Some.AlertV1);

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties.ContainsKey("AISummary"));
            Assert.Equal("\"V1 summary\"", result.Data.Properties["AISummary"].ToString());
        }

        [Fact]
        public async Task EnrichAsync_WhenAlertV2EventType_CallsLlm()
        {
            var app = AttachedLogEventApp();
            app.SummaryToReturn = "V2 summary";
            var evt = Some.AlertLogEvent(eventType: Some.AlertV2);

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties.ContainsKey("AISummary"));
        }

        // ── Property injection: LogEvent ──────────────────────────────────────

        [Fact]
        public async Task EnrichAsync_AlertLogEvent_InjectsAiSummaryProperty()
        {
            var app = AttachedLogEventApp();
            app.SummaryToReturn = "Payment service is down";
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties.ContainsKey("AISummary"));
            var value = Assert.IsType<ScalarValue>(result.Data.Properties["AISummary"]);
            Assert.Equal("Payment service is down", value.Value);
        }

        [Fact]
        public async Task EnrichAsync_AlertLogEvent_ReturnsSameEventWrapper()
        {
            // LogEvent is mutated in place via AddPropertyIfAbsent; the Event<T> wrapper is the same object
            var app = AttachedLogEventApp();
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.Same(evt, result);
        }

        [Fact]
        public async Task EnrichAsync_AlertLogEvent_UsesCustomPropertyName()
        {
            var app = AttachedLogEventApp();
            app.LlmOutputPropertyName = "MyCustomSummary";
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties.ContainsKey("MyCustomSummary"));
            Assert.False(result.Data.Properties.ContainsKey("AISummary"));
        }

        [Fact]
        public async Task EnrichAsync_AlertLogEvent_DoesNotOverwriteExistingProperty()
        {
            var app = AttachedLogEventApp();
            app.SummaryToReturn = "New summary";
            var existingValue = new ScalarValue("Existing summary");
            var evt = Some.AlertLogEvent(extraProperties: new Dictionary<string, LogEventPropertyValue>
            {
                ["AISummary"] = existingValue
            });

            var result = await app.EnrichEventAsync(evt);

            // AddPropertyIfAbsent must not overwrite
            var value = Assert.IsType<ScalarValue>(result.Data.Properties["AISummary"]);
            Assert.Equal("Existing summary", value.Value);
        }

        // ── Property injection: LogEventData ──────────────────────────────────

        [Fact]
        public async Task EnrichAsync_AlertLogEventData_InjectsAiSummaryProperty()
        {
            var app = AttachedLogEventDataApp();
            app.SummaryToReturn = "Database connection pool exhausted";
            var evt = Some.AlertLogEventData();

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties!.ContainsKey("AISummary"));
            Assert.Equal("Database connection pool exhausted", result.Data.Properties["AISummary"]?.ToString());
        }

        [Fact]
        public async Task EnrichAsync_AlertLogEventData_ReturnsNewEventWrapper()
        {
            // LogEventData is immutable — a new Event<T> must be returned
            var app = AttachedLogEventDataApp();
            var evt = Some.AlertLogEventData();

            var result = await app.EnrichEventAsync(evt);

            Assert.NotSame(evt, result);
            Assert.Equal(evt.Id, result.Id);
            Assert.Equal(evt.EventType, result.EventType);
        }

        [Fact]
        public async Task EnrichAsync_AlertLogEventData_PreservesExistingProperties()
        {
            var app = AttachedLogEventDataApp();
            var evt = Some.AlertLogEventData(extraProperties: new Dictionary<string, object?>
            {
                ["Alert.Title"] = "My Alert",
                ["SomeOtherProp"] = 42
            });

            var result = await app.EnrichEventAsync(evt);

            Assert.Equal("My Alert", result.Data.Properties!["Alert.Title"]?.ToString());
            Assert.Equal(42.ToString(), result.Data.Properties["SomeOtherProp"]?.ToString());
            Assert.True(result.Data.Properties.ContainsKey("AISummary"));
        }

        // ── Failure modes ─────────────────────────────────────────────────────

        [Fact]
        public async Task EnrichAsync_WhenLlmThrows_InjectsPlaceholderProperty()
        {
            var app = AttachedLogEventApp();
            app.ExceptionToThrow = new InvalidOperationException("API quota exceeded");
            var evt = Some.AlertLogEvent();

            var result = await app.EnrichEventAsync(evt);

            Assert.True(result.Data.Properties.ContainsKey("AISummary"));
            var value = Assert.IsType<ScalarValue>(result.Data.Properties["AISummary"]);
            Assert.StartsWith("[summary unavailable:", value.Value?.ToString());
            Assert.Contains("API quota exceeded", value.Value?.ToString());
        }

        [Fact]
        public async Task EnrichAsync_WhenLlmThrows_EventIsStillForwarded()
        {
            var app = AttachedLogEventApp();
            app.ExceptionToThrow = new TimeoutException("LLM timed out");
            var evt = Some.AlertLogEvent();

            // Must not throw — failure is swallowed and placeholder injected
            var result = await app.EnrichEventAsync(evt);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task EnrichAsync_WhenLlmThrowsForLogEventData_InjectsPlaceholder()
        {
            var app = AttachedLogEventDataApp();
            app.ExceptionToThrow = new Exception("Network error");
            var evt = Some.AlertLogEventData();

            var result = await app.EnrichEventAsync(evt);

            var summary = result.Data.Properties!["AISummary"]?.ToString();
            Assert.NotNull(summary);
            Assert.StartsWith("[summary unavailable:", summary);
        }
    }
}
