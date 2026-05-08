using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Seq.Apps.LlmEnrichment
{
    class SeqFunctions
    {
        readonly HttpClient _http;
        readonly string _baseUri;

        public SeqFunctions(HttpClient http, string baseUri, string? apiKey)
        {
            _http = http;
            _baseUri = baseUri.TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Seq-ApiKey", apiKey);
        }

        [Description(
            "Search Seq log events within a time range. " +
            "Use this to retrieve the actual events that triggered the alert before writing the summary. " +
            "Extract the time range from the alert metadata in the prompt.")]
        public async Task<string> SearchEvents(
            [Description("Seq filter expression, e.g. \"@Level = 'Error'\". Use an empty string to return all events in the time range.")] string filter,
            [Description("Start of the time range in ISO 8601 UTC format, e.g. '2026-05-08T23:38:00Z'.")] string fromDateUtc,
            [Description("End of the time range in ISO 8601 UTC format, e.g. '2026-05-08T23:39:00Z'.")] string toDateUtc,
            [Description("Maximum number of events to return (1–20).")] int count = 10)
        {
            try
            {
                var url = new StringBuilder($"{_baseUri}/api/events?render=true&count={Math.Clamp(count, 1, 20)}");

                if (!string.IsNullOrWhiteSpace(filter))
                    url.Append($"&filter={Uri.EscapeDataString(filter)}");
                if (!string.IsNullOrWhiteSpace(fromDateUtc))
                    url.Append($"&fromDateUtc={Uri.EscapeDataString(fromDateUtc)}");
                if (!string.IsNullOrWhiteSpace(toDateUtc))
                    url.Append($"&toDateUtc={Uri.EscapeDataString(toDateUtc)}");

                var response = await _http.GetAsync(url.ToString());

                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    return "No events found matching the criteria.";

                var result = new StringBuilder();
                foreach (var evt in root.EnumerateArray())
                {
                    var ts = evt.TryGetProperty("Timestamp", out var t) ? t.GetString() : "";
                    var lv = evt.TryGetProperty("Level", out var l) ? l.GetString() : "";
                    var msg = evt.TryGetProperty("RenderedMessage", out var m) ? m.GetString() : "";
                    var ex = evt.TryGetProperty("Exception", out var e) && e.ValueKind != JsonValueKind.Null
                        ? $"\n  Exception: {e.GetString()}" : "";

                    result.AppendLine($"[{ts}] {lv}: {msg}{ex}");
                }

                return result.ToString().TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
