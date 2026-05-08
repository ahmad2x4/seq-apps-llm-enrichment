# Seq Apps LLM Enrichment — Plan

## Goal

Build a NuGet library that any Seq app can take a dependency on to add **LLM-based summarization of alert events** as an extra event property, which the host app can then render however it wants (HTTP body, Teams card, Slack message, etc.).

---

## Status tracker

### Done

- [x] **Scaffold** — solution, csproj, Directory.Build.props, Build.ps1, sln structure mirroring Datalust convention
- [x] **Base class** — `LlmEnrichedSeqApp<TData>` with all `[SeqAppSetting]` properties, `EnrichAsync` guard logic, property injection for both `LogEvent` and `LogEventData`
- [x] **MAF + OpenAI wiring** — `OnAttached` creates a `ChatClientAgent` via `OpenAIClient.GetChatClient().AsAIAgent()`; `CallLlmAsync` calls `RunAsync` with timeout and failure placeholder
- [x] **Prompt template** — `{RenderedMessage}`, `{Level}`, `{Timestamp}`, and any event property as substitution placeholders; user-configurable via `[SeqAppSetting]`
- [x] **HTTP fork integration** — `base.OnAttached()` + `evt = await EnrichAsync(evt)` + base class swap; `{AISummary}` resolves automatically in ExpressionTemplate body
- [x] **End-to-end test** — alert fires in local Seq (2026.1, .NET 10), LLM summary appears in webhook.site payload
- [x] **README** — library-vs-app distinction, integration steps, settings table, failure behaviour, requirements
- [x] **Tests** — 15 tests covering property injection (both `TData` flavors), failure modes, event type guard, `OnAttached` initialization guard; no real LLM calls via `protected virtual CallLlmAsync`
- [x] **CI/CD** — GitHub Actions on `dev`/`main`; publishes prerelease to NuGet on every `dev` push, stable on `main` merge; `0.1.0-dev-*` prereleases live on nuget.org
- [x] **Branch strategy** — `dev` for day-to-day, `main` protected (PR only) for stable releases

### Backlog

- [x] **Unlist accidental `0.1.0` stable** — confirmed unlisted (NuGet registration API returns 404)

- [ ] **MAF function tools for Seq context** ⬅ next priority — give the LLM tools to query Seq for the actual events that triggered the alert, turning a shallow metadata summary into genuine root-cause analysis:
  - No MCP server needed — MAF's `AIFunctionFactory.Create()` + `[Description]` attributes is sufficient for internal use
  - `SeqApiKey` — one new `[SeqAppSetting]` (optional); Seq server URL comes from `Host.BaseUri` which is already on `SeqApp`, no extra setting needed
  - Initial tool: `SearchEvents(string filter, string fromDateUtc, string toDateUtc, int count)` → calls `GET {Host.BaseUri}/api/events` with `X-Seq-ApiKey` header, returns rendered event messages as a string block
  - Register the tool on the agent via `tools: [AIFunctionFactory.Create(seqFunctions.SearchEvents)]` in `OnAttached`
  - With this the LLM can call `SearchEvents` to pull the actual `GATEWAY_TIMEOUT` error messages, customer IDs, amounts, etc. before writing the summary
  - Reference: MAF function tools docs — https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools?pivots=programming-language-csharp
  - Reference: Seq HTTP API — https://datalust.co/docs/server-http-api (`/api/events` endpoint)

- [ ] **Multi-provider support** — add a provider selector setting and init path for each MAF-supported provider:
  - `OpenAI` — done (default)
  - `Azure OpenAI` — MAF supports it natively; needs `LlmEndpoint` setting
  - `Anthropic` — officially supported in MAF; needs its NuGet package
  - `Ollama` — MAF supports it; useful for on-prem/air-gapped deployments
  - Reference: https://learn.microsoft.com/en-us/agent-framework/agents/providers/?pivots=programming-language-csharp

- [ ] **Publish stable `0.1.0`** — after MCP and multi-provider land and API is settled; merge `dev` → `main` via PR
- [ ] **PR to datalust/seq-app-httprequest** — backwards-compatible (no-op when unconfigured); strong case for upstream merge
- [ ] **Repeat fork pattern** for Teams / Slack / Email apps

---

## Scope decisions (locked in)

| Decision | Choice | Reason |
|---|---|---|
| When does the LLM fire? | **Alerts only** (`EventType == 0xA1E77000` or `0xA1E77001`) | Cost & latency unacceptable for streamed log events |
| Failure mode | **Forward with placeholder** `[summary unavailable: <reason>]` | Graceful degradation; never drop an alert |
| Caching | **None for v1** | Keep it simple; revisit if cost/perf demands it |
| Property injection | **Generic helper** injecting a new property (`AISummary` by default) | Single integration line per fork; works across both event data flavors |
| Prompt | **User-configurable via `[SeqAppSetting]`** | Different teams need different summary styles |
| LLM provider abstraction | **Microsoft Agent Framework** (MAF, `Microsoft.Agents.AI` v1.3.0) | Provider-agnostic; Anthropic, Azure OpenAI, Ollama all supported natively |
| Initial provider | **OpenAI** (`Microsoft.Agents.AI.OpenAI`) | Simplest first target |
| Runtime requirement | **.NET 10 / Seq 2026.1+** | MAF depends on `System.Text.Json 10.x`; Seq loads apps in-process so the host runtime must provide it |

---

## Architecture

### Library

**Repo:** `/Users/ahmadreza/source/seq-apps-llm-enrichment`

```
seq-apps-llm-enrichment/
├── src/Seq.Apps.LlmEnrichment/
│   ├── Seq.Apps.LlmEnrichment.csproj
│   └── LlmEnrichedSeqApp.cs
├── test/Seq.Apps.LlmEnrichment.Tests/
│   └── Seq.Apps.LlmEnrichment.Tests.csproj
├── Directory.Build.props          ← version lives here, bump on every change
├── Build.ps1
├── CLAUDE.md                      ← dev loop rules
├── README.md
└── Seq.Apps.LlmEnrichment.sln
```

**Public API surface:**
```csharp
public abstract class LlmEnrichedSeqApp<TData> : SeqApp
{
    // [SeqAppSetting] — all optional
    public string? LlmApiKey { get; set; }
    public string? LlmModel { get; set; }
    public string? LlmPromptTemplate { get; set; }
    public string? LlmOutputPropertyName { get; set; }  // default "AISummary"
    public int? LlmTimeoutSeconds { get; set; }          // default 30

    // Single integration point for host apps:
    protected Task<Event<TData>> EnrichAsync(Event<TData> evt);
}
```

**`EnrichAsync` behaviour:**
1. `_agent == null` (LLM not configured) → return `evt` unchanged
2. `evt.EventType` is not `0xA1E77000` / `0xA1E77001` → return `evt` unchanged
3. Build prompt from template + event properties → call LLM via MAF
4. Success → inject `AISummary = "<summary>"` into the event
5. Failure/timeout → inject `AISummary = "[summary unavailable: <reason>]"`, log warning

**Property injection:**
- `Serilog.Events.LogEvent` → `AddPropertyIfAbsent(new LogEventProperty(...))`
- `Seq.Apps.LogEvents.LogEventData` → clone with new `Properties` dictionary, wrap in new `Event<TData>`

### HTTP fork

**Repo:** `/Users/ahmadreza/source/seq-app-httprequest`

Integration — four lines total:
```csharp
// 1. using
using Seq.Apps.LlmEnrichment;

// 2. base class
public class HttpApp : LlmEnrichedSeqApp<LogEvent>, ISubscribeToAsync<LogEvent>

// 3. OnAttached
protected override void OnAttached()
{
    base.OnAttached(); // ← must be first
    // ... existing factory setup
}

// 4. OnAsync
public async Task OnAsync(Event<LogEvent> evt)
{
    evt = await EnrichAsync(evt);
    // ... existing HTTP dispatch
}
```

---

## Infrastructure notes

- **Seq container:** `anility-financial-assessment-seq-1`, image `datalust/seq:2026.1.16173-pre` (.NET 10.0.5)
- **Seq UI:** http://localhost:8080
- **Data volume:** `.seq/` → `/data` inside container; local NuGet feed at `/data/local-nuget`
- **Dev loop:** see `CLAUDE.md` — always bump `Directory.Build.props` version and clear `~/.nuget/packages/seq.apps.llmenrichment/` before rebuilding the fork

---

## References

- seq-apps-runtime: https://github.com/datalust/seq-apps-runtime
- seq-app-httprequest (upstream): https://github.com/datalust/seq-app-httprequest
- seq-app-teams (LogEventData reference): https://github.com/AntoineGa/Seq.App.Teams
- MAF providers: https://learn.microsoft.com/en-us/agent-framework/agents/providers/?pivots=programming-language-csharp
- Alert event type magic numbers: `0xA1E77000` (V1), `0xA1E77001` (V2) — confirmed working in Seq 2026.1
