# Seq.Apps.LlmEnrichment

A **.NET library** that adds LLM-based summarization to any Seq app. This is **not a standalone Seq app** — it is a base class library that other Seq apps take a dependency on.

When an alert event arrives, the library calls an LLM, generates a concise summary, and injects it as a new property (default: `AISummary`) on the event before the host app processes it. If the LLM is not configured, or the event is not an alert, the event passes through unchanged.

## How it works

```
Seq alert fires
       │
       ▼
  Host Seq app (e.g. HTTP, Teams, Slack)
  OnAsync(evt)
       │
       ▼
  EnrichAsync(evt)          ← provided by this library
  ├─ Not an alert?  ──────► return evt unchanged
  ├─ LLM not configured? ► return evt unchanged
  ├─ Call LLM (OpenAI) ──► AISummary = "Payment service ..."
  └─ LLM fails? ─────────► AISummary = "[summary unavailable: ...]"
       │
       ▼
  evt now has AISummary property
       │
       ▼
  Host app sends notification
  (HTTP body, Teams card, Slack message, etc.)
  with {AISummary} resolved automatically
```

## Integrating into a Seq app

Three changes to any existing Seq app:

**1. Add the package reference**
```xml
<PackageReference Include="Seq.Apps.LlmEnrichment" Version="0.1.2" />
```

**2. Change the base class**
```csharp
// Before
public class MyApp : SeqApp, ISubscribeToAsync<LogEvent>

// After
public class MyApp : LlmEnrichedSeqApp<LogEvent>, ISubscribeToAsync<LogEvent>
```

**3. Call `base.OnAttached()` and `EnrichAsync` in `OnAsync`**
```csharp
protected override void OnAttached()
{
    base.OnAttached(); // required — initializes the LLM agent
    // ... rest of your OnAttached
}

public async Task OnAsync(Event<LogEvent> evt)
{
    evt = await EnrichAsync(evt);
    // ... rest of your OnAsync
}
```

That's it. The `AISummary` property is now available on the event for use in any template, e.g. `{AISummary}` in an HTTP body or Teams card.

> The library works with both `Serilog.Events.LogEvent` (HTTP app) and `Seq.Apps.LogEvents.LogEventData` (Teams, Slack, most community apps).

## Settings exposed to the host app

These settings appear automatically in the Seq app instance configuration UI alongside the host app's own settings:

| Setting | Required | Default | Description |
|---|---|---|---|
| LLM API Key | No | — | OpenAI API key. Leave blank to disable enrichment entirely. |
| LLM Model | No | — | Model name, e.g. `gpt-4o-mini`. Leave blank to disable. |
| LLM Prompt Template | No | See below | Prompt sent to the LLM. Supports `{RenderedMessage}`, `{Level}`, `{Timestamp}`, and any event property as placeholders. |
| AI Summary Property Name | No | `AISummary` | Name of the property injected into the event. |
| LLM Timeout (seconds) | No | `30` | Maximum wait time for an LLM response. |

**Default prompt template:**
```
Summarize this Seq alert:

Message: {RenderedMessage}
Level: {Level}
Timestamp: {Timestamp}
```

## Failure behaviour

The library never drops an alert. On any failure (timeout, API error, network issue) the `AISummary` property is set to `[summary unavailable: <reason>]` and a warning is logged to the Seq app's diagnostic log. The host app continues normally.

## Requirements

- .NET 8 or later
- Seq running on .NET 10 (Seq 2026.1+) — required because the LLM SDK dependencies use `System.Text.Json 10.x`, which must be provided by the host runtime
- An OpenAI API key

## What's out of scope

- This library only enriches events — it never decides whether to forward them
- Caching / deduplication (v1)
- Multi-provider support beyond OpenAI (planned via Microsoft Agent Framework abstraction)
- `ISubscribeToJsonAsync` consumers (raw CLEF JSON)
