# Seq.Apps.LlmEnrichment

## Dev loop — deploying library changes to a local Seq instance

Every time you change the library, you must bump the version and clear the NuGet cache before rebuilding the fork. Otherwise `dotnet publish` silently uses the stale cached DLL and the changes never reach Seq.

**Steps (run from repo root):**

1. Bump `<VersionPrefix>` in `Directory.Build.props` (e.g. `0.1.2` → `0.1.3`)
2. Pack the library:
   ```bash
   dotnet pack src/Seq.Apps.LlmEnrichment -c Release -o ~/local-nuget
   ```
3. Clear the NuGet cache for this library:
   ```bash
   rm -rf ~/.nuget/packages/seq.apps.llmenrichment/
   ```
4. In `seq-app-httprequest`, update the `<PackageReference>` version to match, then restore, publish, and repack with a new suffix (e.g. `dev4`):
   ```bash
   dotnet restore src/Seq.App.HttpRequest --source ~/local-nuget --source https://api.nuget.org/v3/index.json
   dotnet publish src/Seq.App.HttpRequest -c Release -o src/Seq.App.HttpRequest/obj/publish
   dotnet pack src/Seq.App.HttpRequest -c Release --no-build --version-suffix dev4 -o ~/local-nuget
   ```
5. Copy both nupkg files to the Seq data folder:
   ```bash
   cp ~/local-nuget/Seq.Apps.LlmEnrichment.<version>.nupkg \
      ~/local-nuget/Seq.App.HttpRequest.1.0.1-dev4.nupkg \
      /Users/ahmadreza/source/anility/anility-financial-assessment/.seq/local-nuget/
   ```
6. In Seq UI: **Settings → Apps → HTTP → Manage → Update** → pick the new version → **Save** the instance.

## Seq infrastructure

- Seq runs in Docker: container `anility-financial-assessment-seq-1`, image `datalust/seq:2026.1.16173-pre` (.NET 10)
- Web UI: http://localhost:8080
- Data volume: `/Users/ahmadreza/source/anility/anility-financial-assessment/.seq` → `/data` inside the container
- Local NuGet feed (inside container): `/data/local-nuget`
- Feed must be registered in Seq UI as an absolute path: `/data/local-nuget`

## Why .NET 10 Seq is required

MAF (`Microsoft.Agents.AI`) and the Anthropic SDK both depend on `System.Text.Json 10.x`. Seq loads apps into its own process, so the host runtime must provide STJ 10.x. Seq 2025.x runs on .NET 9 (STJ 9.x) and causes an assembly manifest mismatch. Seq 2026.1+ runs on .NET 10 and works correctly.
