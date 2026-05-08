using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Seq.Apps;
using Seq.Apps.LogEvents;
using Serilog.Events;

namespace Seq.Apps.LlmEnrichment
{
    public abstract class LlmEnrichedSeqApp<TData> : SeqApp
    {
        const uint AlertV1 = 0xA1E77000;
        const uint AlertV2 = 0xA1E77001;
        const string DefaultOutputPropertyName = "AISummary";
        const int DefaultTimeoutSeconds = 30;

        const string AgentInstructions =
            "You are a senior site reliability engineer analysing a Seq alert. " +
            "You have access to a SearchEvents tool — always call it first to retrieve the actual log events " +
            "from the alert time range before writing the summary. " +
            "Use the time range and alert title from the prompt to choose an appropriate filter. " +
            "Then write a 2-3 sentence incident summary covering: what is failing and why (use specific details " +
            "from the retrieved events), the business impact, and the single most important immediate action. " +
            "Never say 'investigate further' — be concrete. No greetings or closing remarks.";

        const string DefaultPromptTemplate =
            "Alert \"{Alert.Title}\" has triggered in production.\n\n" +
            "Event data:\n{EventProperties}\n\n" +
            "Write a 2-3 sentence incident summary covering: what is failing and likely why, " +
            "the business impact, and the single most important immediate action. " +
            "Be specific and technical. Never say \"investigate further\" — give a concrete first step.";

        AIAgent? _agent;

        [SeqAppSetting(
            InputType = SettingInputType.Password,
            IsOptional = true,
            DisplayName = "LLM API Key",
            HelpText = "API key for the LLM provider (e.g. OpenAI). Leave blank to disable enrichment.")]
        public string? LlmApiKey { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "LLM Model",
            HelpText = "Model to use, e.g. `gpt-4o-mini`. Leave blank to disable enrichment.")]
        public string? LlmModel { get; set; }

        [SeqAppSetting(
            InputType = SettingInputType.LongText,
            IsOptional = true,
            DisplayName = "LLM Prompt Template",
            HelpText = "Prompt sent to the LLM. Use `{RenderedMessage}`, `{Level}`, `{Timestamp}`, and any " +
                       "event property name as placeholders, e.g. `{Alert.Title}`. Leave blank to use the default prompt.")]
        public string? LlmPromptTemplate { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "AI Summary Property Name",
            HelpText = "Name of the property added to the event. Defaults to `AISummary`.")]
        public string? LlmOutputPropertyName { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "LLM Timeout (seconds)",
            HelpText = "Maximum time to wait for an LLM response before giving up. Defaults to 30 s.")]
        public int? LlmTimeoutSeconds { get; set; }

        [SeqAppSetting(
            InputType = SettingInputType.Password,
            IsOptional = true,
            DisplayName = "Seq API Key",
            HelpText = "Seq API key used to query events for richer alert context. " +
                       "Leave blank to attempt anonymous access (works when Seq allows unauthenticated reads).")]
        public string? SeqApiKey { get; set; }

        protected override void OnAttached()
        {
            if (string.IsNullOrWhiteSpace(LlmApiKey) || string.IsNullOrWhiteSpace(LlmModel))
                return;

            var seqFunctions = new SeqFunctions(new HttpClient(), Host.BaseUri, SeqApiKey);
            Func<string, string, string, int, Task<string>> searchFn = seqFunctions.SearchEvents;
            var searchTool = AIFunctionFactory.Create(searchFn);

            _agent = new OpenAIClient(LlmApiKey!)
                .GetChatClient(LlmModel!)
                .AsAIAgent(
                    instructions: AgentInstructions,
                    name: "SeqAlertSummarizer",
                    tools: [searchTool]);
        }

        protected async Task<Event<TData>> EnrichAsync(Event<TData> evt)
        {
            if (_agent == null)
                return evt;

            if (evt.EventType != AlertV1 && evt.EventType != AlertV2)
                return evt;

            var propertyName = string.IsNullOrWhiteSpace(LlmOutputPropertyName)
                ? DefaultOutputPropertyName
                : LlmOutputPropertyName!;

            string summary;
            try
            {
                summary = await CallLlmAsync(evt);
            }
            catch (Exception ex)
            {
                summary = $"[summary unavailable: {ex.Message}]";
                Log.Warning(ex, "LLM enrichment failed for event {EventId}", evt.Id);
            }

            return InjectProperty(evt, propertyName, summary);
        }

        protected virtual async Task<string> CallLlmAsync(Event<TData> evt)
        {
            var prompt = BuildPrompt(evt);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ResolvedTimeoutSeconds));
            var response = await _agent!.RunAsync(prompt, session: null, options: null, cts.Token);
            return response.Text ?? "[no response from LLM]";
        }

        private string BuildPrompt(Event<TData> evt)
        {
            var template = string.IsNullOrWhiteSpace(LlmPromptTemplate)
                ? DefaultPromptTemplate
                : LlmPromptTemplate!;

            var properties = ExtractProperties(evt);

            // Built-in: formats all event properties as a readable key: value block,
            // excluding low-signal built-ins that are already available as individual placeholders.
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "RenderedMessage", "Level", "Timestamp" };

            properties["EventProperties"] = string.Join("\n", properties
                .Where(kv => !excluded.Contains(kv.Key))
                .Select(kv => $"{kv.Key}: {kv.Value}"));

            return Regex.Replace(template, @"\{([\w.]+)\}", m =>
            {
                var key = m.Groups[1].Value;
                return properties.TryGetValue(key, out var val) ? val : m.Value;
            });
        }

        private static Dictionary<string, string> ExtractProperties(Event<TData> evt)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            switch (evt.Data)
            {
                case LogEvent logEvent:
                    result["RenderedMessage"] = logEvent.RenderMessage();
                    result["Level"] = logEvent.Level.ToString();
                    result["Timestamp"] = logEvent.Timestamp.ToString("O");
                    foreach (var (key, value) in logEvent.Properties)
                        result[key] = value.ToString();
                    break;

                case LogEventData logEventData:
                    result["RenderedMessage"] = logEventData.RenderedMessage ?? "";
                    result["Level"] = logEventData.Level.ToString();
                    result["Timestamp"] = logEventData.LocalTimestamp.ToString("O");
                    if (logEventData.Properties != null)
                        foreach (var (key, value) in logEventData.Properties)
                            result[key] = value?.ToString() ?? "";
                    break;
            }

            return result;
        }

        private static Event<TData> InjectProperty(Event<TData> evt, string propertyName, string value)
        {
            switch (evt.Data)
            {
                case LogEvent logEvent:
                    logEvent.AddPropertyIfAbsent(new LogEventProperty(propertyName, new ScalarValue(value)));
                    return evt;

                case LogEventData logEventData:
                {
                    var props = new Dictionary<string, object?>(
                        (IReadOnlyDictionary<string, object?>?)logEventData.Properties
                            ?? new Dictionary<string, object?>());
                    props[propertyName] = value;

                    var enriched = new LogEventData
                    {
                        Id = logEventData.Id,
                        LocalTimestamp = logEventData.LocalTimestamp,
                        Level = logEventData.Level,
                        MessageTemplate = logEventData.MessageTemplate,
                        RenderedMessage = logEventData.RenderedMessage,
                        Exception = logEventData.Exception,
                        Properties = props
                    };
                    return new Event<TData>(evt.Id, evt.EventType, evt.Timestamp, (TData)(object)enriched);
                }

                default:
                    return evt;
            }
        }

        protected int ResolvedTimeoutSeconds =>
            LlmTimeoutSeconds is > 0 ? LlmTimeoutSeconds.Value : DefaultTimeoutSeconds;
    }
}
