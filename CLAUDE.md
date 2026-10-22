# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repository contains a modern C# MCP (Model Context Protocol) server for interactive Windows memory dump analysis using Microsoft's Command Line Debugger (cdb.exe) or WinDbg. The system uses a **single-process architecture** where DumpAnalysisService provides both MCP HTTP endpoints (for Claude Code/MCP clients) and REST API endpoints (for PowerShell scripts, Azure Functions, or custom clients), managing long-running CDB debugging sessions with job-based async operations.

## Build and Development Commands

```powershell
# Build single-file executables (recommended for distribution)
.\Scripts\Publish.ps1

# Development builds
dotnet build

# Run tests
dotnet test
dotnet test --filter "FullyQualifiedName~AnalysisService"  # Run specific test class

# Development: Run DumpAnalysisService
dotnet run --project DumpAnalysisService

# Run on custom port (default: 7997)
dotnet run --project DumpAnalysisService -- 7997

# Test REST API endpoints
curl http://localhost:7997/api/jobs
curl http://localhost:7997/api/diagnostics/analyses
```

## Architecture Overview - Single Process + Job-Based System

**Problem**: CDB dump loading and symbol resolution can take **several minutes**. MCP clients expect immediate responses and need progress updates.

**Solution**: Single ASP.NET Core service with dual interfaces (MCP Streamable HTTP + REST API) and job-based async operations:

![Architecture](docs/architecture/architecture.svg)

The diagram is rendered from [`docs/architecture/architecture.d2`](docs/architecture/architecture.d2), the
source of truth for components, connections and trust/process boundaries (see **Diagrams** below).
Sequence diagrams for the `load_dump` job and the request security path are in
[`docs/architecture/README.md`](docs/architecture/README.md).

### Diagrams

- **D2 is the architecture source of truth.** `docs/architecture/architecture.d2` (ELK layout) holds the
  components, their connections and the boundaries: clients, the DumpAnalysisService process, one cdb.exe
  per dump, the Windows host or container. Its `#` comments carry the details the picture leaves out.
  Read it before changing how components talk to each other.
- When a component, boundary or connection changes, edit the `.d2`, run `Scripts/render-diagrams.sh`
  (formats, validates, renders every `docs/**/*.d2`) and commit the regenerated `.svg` in the same PR.
  The CI `diagrams` job runs it with `--check` and fails on an unformatted or invalid source or a stale SVG.
- Install D2 the way CI does, `go install oss.terrastruct.com/d2@v0.7.2`: a release binary embeds a
  different version string, so every SVG would read as stale.
- **Mermaid is for flows over time** (sequence diagrams), inline in Markdown, as in
  `docs/architecture/README.md`. Update them when the flow they show changes.
- No ASCII art or draw.io for architecture. In D2 text, a `;` ends a statement, so keep it out of labels.

### Projects Structure

Package versions are managed centrally in `Directory.Packages.props`; shared build
settings (TFM, nullable, NuGetAudit with High/Critical advisories as errors, Apache-2.0
package metadata) live in `Directory.Build.props`. Licence: Apache-2.0 (`LICENSE`, `NOTICE`).

1. **DumpAnalysisService** - All-in-One: MCP Server + REST API + Debugging Engine
   - Entry Point: `Program.cs` - ASP.NET Core Web API (port 7997)
   - **MCP Integration**: ModelContextProtocol.AspNetCore SDK
     - Streamable HTTP transport at `/mcp` (`app.MapMcp("/mcp")`)
     - Tools: `DebuggerTools.cs` with 11 [McpServerTool] methods
   - **Services**: Session management, job tracking, CDB process orchestration
   - **Controllers**: REST API for PowerShell/Azure Functions integration
   - **SignalR Hub**: Real-time progress notifications at `/hubs/progress`

2. **Shared** - Shared models, contracts, and utilities
   - Configuration models (`SymbolsConfiguration`, `DebuggerConfiguration`)
   - API contracts (`ApiContracts.cs`)
   - Constants (ports, timeouts, MCP error codes)
   - Client libraries (`DebuggerApiService`, `SignalRClientService`)

3. **CommandLineClient** - Command-line client for scripting
   - Standalone executable for PowerShell scripts and Azure Functions
   - Uses Shared.Client libraries to communicate with DumpAnalysisService

4. **Test Projects**
   - `DumpAnalysisService.Tests`: Service-layer unit tests for `DumpAnalysisService` (session manager, job manager, analysis service, controllers). `*PropertyTests` classes are FsCheck property tests that fuzz untrusted input: `MinidumpParser` with mutated, truncated and random dumps (parse, or `InvalidDataException`; triage builds from whatever parses), `CdbCommandPolicy` and `SymbolInputValidator` (accept or `ArgumentException`, and what they accept keeps their invariants), and `CrashFingerprinter` (any text parses; addresses, offsets, templates, arguments and module case do not change a fingerprint). OpenSSF Scorecard counts FsCheck as fuzzing for C#.
   - `Shared.Tests`: Unit tests for the `Shared` project (configuration providers, client libraries, constants). Some SignalR-dependent tests are skipped under xUnit when no host is running.
   - `DumpAnalysisService.IntegrationTests`: Integration suite that boots `DumpAnalysisService` in-process via `WebApplicationFactory` and exercises the REST + SignalR flow against a real `TestCrasher` dump.
   - `DumpAnalysisService.TestCrasher`: Tiny console app that writes a snapshot dump of itself (heap, threads) for the integration tests (copied next to the test output via a `CopyTestCrasher` MSBuild target).
   - `DumpAnalysisService.CrashScenarios`: NativeAOT program with one real crash per scenario (access violation via two call paths, divide by zero, stack overflow, custom `RaiseException` code, fail-fast, unhandled .NET exception, critical-section deadlock, call into an unloaded DLL, use-after-free fill pattern, C++ `std::out_of_range` from msvcp140, busy loop, handle leak). NativeAOT gives native frames with our own PDB, so cdb shows our function names. It writes no dumps: `CrashDumpFixture` runs it under cdb with `sxe -c ".dump /ma ..."` for each scenario-ending event. Published next to the tests by the Windows-only `PublishCrashScenarios` target (NativeAOT cannot cross-compile); elsewhere the crash tests skip.

### Docker Distribution

Pre-built Windows container images are published to **`ghcr.io/janecekvit/mcp-windbg`** on every `v*.*.*` release tag (workflow: `.github/workflows/build-and-release.yml`).

- **Base image:** `mcr.microsoft.com/windows/servercore:ltsc2022` (Linux containers cannot run `cdb.exe`).
- **Debugging tools:** installed during image build via the Windows SDK web setup (`/features OptionId.WindowsDesktopDebuggers`), pinned to a versioned download URL and its SHA-256 and required to carry a valid Microsoft Authenticode signature. Dependabot does not update it: to move to a newer SDK, resolve `fwlink/?linkid=2237387` and update the URL and hash in the `Dockerfile`.
- **Build input:** the existing `publish/win-x64/` self-contained publish output — Docker layer simply `COPY`s the same artefacts the Release ZIP ships. No second build path.
- **Health probe:** `HEALTHCHECK` polls `GET /api/diagnostics/health`, the one `[AllowAnonymous]` route, so the probe keeps working when `Security__ApiKey` or `Security__Jwt__*` is set.
- **Exposure:** the image sets `AllowedHosts=*`; set `Security__ApiKey` whenever the port is reachable from other machines.
- **CI gate:** two jobs on `windows-2022` (`windows-latest` has no running Docker engine; the ltsc2022 base matches that host), both from the build job's `publish/win-x64` artifact. `docker` builds the image on every pull request and push to main, without logging in or pushing, so Dockerfile and base image digest changes are tested before a release. `docker-publish` builds and pushes on tag refs only (`startsWith(github.ref, 'refs/tags/v')`) and is the only job with `packages: write`. A Docker failure never blocks the release ZIP.

See `Dockerfile`, `.dockerignore`, and the README section "Running via Docker" for the user-facing flow.

## Security Model (DumpAnalysisService/Security/)

`SecurityOptions` is bound from the `Security` configuration section:
- **Authentication** (`DumpAuthentication.AddDumpAuthentication`): a policy scheme forwards a
  JWT-shaped bearer token to JwtBearer (`Security:Jwt`, enabled by `Authority`) and anything else
  to `ApiKeyAuthenticationHandler` (`Security:ApiKey`, `X-Api-Key` or `Authorization: Bearer`,
  constant-time). SignalR may use `?access_token=` on `/hubs`. With JWT on, challenges go to the
  MCP SDK's `McpAuthenticationHandler` (`AddMcp`), which adds `resource_metadata` to the 401 and
  serves RFC 9728 metadata at `/.well-known/oauth-protected-resource/...`. When any credential is
  configured, a fallback policy requires an authenticated user everywhere except
  `GET /api/diagnostics/health`.
- **Authorization**: `[Authorize(Policy = DumpPolicies.Read|Analyze|Admin)]` on controllers,
  the hub and each MCP tool (`.AddAuthorizationFilters()` also filters `tools/list`).
  `DumpPermissionHandler` grants everything when no credential is configured, and to the API key;
  for tokens, `Caller.PermissionOf` reads `Jwt:RoleClaimTypes` (default `roles` only).
- **Ownership** (`Security/Ownership.cs`): jobs (`CreateJob(..., owner)`) and sessions
  (`CreateSessionWithDumpAsync(..., owner)`) record `Caller.Id` (`oid`, else `sub`; null for
  unrestricted callers: no auth configured, the API key, admins). Entry points (JobsController,
  DebuggerTools, ProgressHub.SubscribeToJob) call `EnsureSessionAccess` / `CanAccessJob` before
  starting work, so another user's session or job reads as not found. MCP tools get the caller
  from `McpCallerContext`, which a call-tool request filter (Program.cs) sets from
  `request.User`. The filter also runs for task-backed calls, after the HTTP request has ended.
  Never take the user from IHttpContextAccessor or from a `RequestContext` tool parameter: when
  several servers build their tools at once in one process, the SDK can lose a special
  parameter binding, and the parameter then arrives empty from the tool arguments. With
  authentication on, a missing principal is `Caller.Unidentified` (no permission), never
  unrestricted. `AuditLog.Record` logs who did what.
- Tests: `DumpAnalysisService.Tests/Security/AuthTestHost.cs` boots the service with tokens signed
  by a local RSA key (static OpenID configuration, nothing fetched). Settings go through
  `UseSetting`, because `Program` reads `Security:*` before `Build()`. Classes that start it
  share the non-parallel `InProcessServiceCollection`, for the same SDK binding race.
- **`CdbCommandPolicy`**: applied to user commands in
  `SessionManagerService.ExecuteCommandAsync` only (never to internal init or
  predefined-analysis commands). Single line, no `q`/`qq`, and unless
  `AllowUnsafeCommands` no `.shell`/`.load`/`.create`/`.writemem`/`$<`/… anywhere
  in the line. cdb.exe also starts with `-noshell`.
- **`SymbolInputValidator`**: client symbol values (headers or REST body) are
  validated in `CdbSessionFactory`; `AllowClientSymbolOverrides=false` drops them.
- **`AllowedDumpDirectories`**, **`MaxConcurrentSessions`**: enforced in
  `SessionManagerService.CreateSessionWithDumpAsync`.
- `AllowedHosts` defaults to localhost (DNS-rebinding protection); the Docker
  image sets `AllowedHosts=*` and expects `Security__ApiKey`.

When adding an endpoint or MCP tool, give it a `DumpPolicies` policy and, if it takes a session
or job id, an ownership check. When adding a new way to send text to cdb.exe, route user input through
`CdbCommandPolicy` and never build a command line by string concatenation:
`CdbProcessManager.CreateStartInfo` uses `ProcessStartInfo.ArgumentList`.

## CI and supply chain

Workflows: `build-and-release.yml` (build, tests, coverage, Docker image build; release ZIP and image push on tags),
`security.yml` (CodeQL `actions` + `csharp` with `security-extended`, zizmor; push, PR, weekly),
`scorecard.yml` (OpenSSF Scorecard, published, README badge). Rules for every workflow:

- **Pin actions** to a full commit SHA with the release in a comment:
  `uses: actions/checkout@<sha> # v7.0.1`. A pinned release must be at least 14 days old; resolve
  the SHA with `git ls-remote` and check the tag's date. Dependabot (`cooldown: 14` days) keeps the
  pins, the NuGet packages and the Dockerfile base image digest current.
- `persist-credentials: false` on every checkout.
- Top-level `permissions: contents: read` (`{}` in `scorecard.yml`); a job that needs more asks for
  it at job level, with a comment saying why (`id-token: write` for Codecov OIDC, `checks: write`
  for the test reporter, `security-events: write` for SARIF, `packages: write` for ghcr.io,
  `contents: write` only in the release job, which also has `id-token: write` and
  `attestations: write` for the build provenance).
- **Signed releases**: the release job attests the ZIP with `actions/attest-build-provenance`
  (Sigstore) and attaches the bundle as `.sigstore.json` and its DSSE envelope as `.intoto.jsonl`,
  the suffixes OpenSSF Scorecard's Signed-Releases check looks for.
- **Never expand `${{ }}` inside a `run:` script**: pass the value through `env:` and read
  `$env:NAME` / `$NAME` (zizmor flags it as template injection).
- A `concurrency` group in every workflow; pull request runs cancel their older run.
- The symbol cache is skipped on tag builds, so a release never restores a cache another run wrote
  (the one `zizmor: ignore[cache-poisoning]`).
- Before pushing a workflow change, run `actionlint` and `zizmor --offline .github/workflows`
  (default persona clean, `--persona auditor` without warnings).

Repository conventions:

- **The repository is public: never put links to Claude sessions (`claude.ai/code/session_…`) or
  other private URLs in commits, pull request descriptions, comments or files.** No
  `Claude-Session:` commit trailers; `Co-Authored-By: Claude …` stays.
- **Pull requests are squash-merged with the title and description as the commit message, so
  write the description as that message (what and why, plus how it was tested); reviewer or
  settings instructions go in a PR comment.**
- Vulnerabilities are reported privately (`SECURITY.md`); `.github/copilot-instructions.md` lists
  what blocks a change in Copilot code review (keep it under 4,000 characters).

## Critical Implementation Details

### Symbol Configuration Architecture (3-Tier Priority)

**Priority Order** (DumpAnalysisService/Tools/DebuggerTools.cs):
1. **Tool Parameters** (highest) - Per-call override via MCP tool arguments
2. **HTTP Headers** - Per-MCP-client via `.mcp.json` headers (X-Symbol-Cache, X-Symbol-Path-Extra, X-Symbol-Servers)
3. **appsettings.json** - Server-wide defaults (DefaultSymbolCache, DefaultSymbolPathExtra, DefaultSymbolServers)

**Implementation**:
```csharp
// DebuggerTools.cs - LoadDump tool
var providerConfig = _symbolConfigProvider.GetConfiguration(); // Reads HTTP headers
var symbols = new SymbolsConfiguration(
    SymbolCache: symbol_cache ?? providerConfig.SymbolCache,           // Tool param overrides
    SymbolPathExtra: symbol_path_extra ?? providerConfig.SymbolPathExtra,
    SymbolServers: symbol_servers ?? providerConfig.SymbolServers);
```

**HttpHeaderSymbolConfigurationProvider** (DumpAnalysisService/Providers/):
- Scoped service (per HTTP request)
- Reads `X-Symbol-Cache`, `X-Symbol-Path-Extra`, `X-Symbol-Servers` headers
- Accessed via `IHttpContextAccessor`

### Asynchronous Command Execution (CdbSessionService.cs)

CDB commands execute asynchronously with **marker-based output parsing**:

```csharp
// CdbProcessManager.ExecuteCommandAsync writes three stdin lines:
//   .echo __START_COMMAND_<guid>__
//   <command>
//   .echo __END_COMMAND_<guid>__
// A background pump drains stdout into a Channel; CdbOutputCollector keeps only
// the text between the markers and strips CDB prompts. stderr is drained to the log.
// Timeout: 15 minutes (Constants.Debugging.SymbolLoadingTimeoutMinutes)
```

**Why markers?** CDB output is continuous and unpredictable. Markers provide reliable command completion detection; the START marker discards output left over from an earlier timed-out command, and separate lines keep a `*` comment or unbalanced quote in the command from swallowing the END marker.

**Thread Safety**:
- `SemaphoreSlim` (CdbSessionService.cs:20) ensures only 1 command per session at a time
- `ConcurrentDictionary` (SessionManagerService.cs:13) allows multiple sessions concurrently
- Each dump file gets isolated CDB process - no cross-session interference

### Session Lifecycle

```
CreateSessionWithDumpAsync() (SessionManagerService.cs)
  → Generate 8-char session ID
  → CdbSessionService.LoadDumpAsync()
    → Process.Start("cdb.exe -z dump.dmp")
    → InitializeSessionAsync() - symbol setup
      → Configure symbol options, add symbol servers
      → .reload with retry logic for symbol failures
  → Store in _sessions dictionary (limit-checked; removed + disposed if loading fails)

ExecuteCommandAsync() (CdbSessionService.cs)
  → Acquire semaphore lock
  → CdbProcessManager.ExecuteCommandAsync (start/end markers, timeout)
  → Release semaphore

CancelSessionAsync() (SessionManagerService.cs)
  → Call session.CancelAsync()
  → Send "q" to CDB stdin
  → WaitForExit(5s) or Kill()
  → Remove from dictionary, Dispose()
```

### Dump Triage (Infrastructure/Minidump/, Services/DumpTriageService.cs)

`MinidumpParser` reads the minidump header, stream directory and the metadata streams by
offset: system info, misc info, exception (plus the faulting thread's x64/x86 registers from
its CONTEXT), thread list, module list, unloaded modules, thread info, thread names, memory
info (regions), handle data, process VM counters, system memory info and comments. Captured
memory (memory list / memory64 list) is only touched by small reads in `MemoryMap`: the C++
exception type and message (`CppExceptionDecoder`, ThrowInfo RVAs on x64, pointers on x86)
and the delay-load DLL/function. Kernel dumps get only the `PAGEDU64`/`PAGEDUMP` header
(bug check, DumpType, SystemTime, SystemUpTime). Every offset and count is bounds-checked
and malformed files raise `InvalidDataException`; list streams are stepped by their own
SizeOfEntry. Struct offsets were checked against the Windows headers (Wine/mingw-w64).
`DumpTriageService.Build` is a pure mapping to `Shared.Models.DumpTriageResult`: a stable
`fault.classification` (`ExceptionCodes.ClassOf` plus the access violation refinements),
fill patterns (`FillPatterns`), fail-fast codes (`FastFailCodes`), bug check summaries
(`BugCheckCodes.Describe`) and the hints. `WithSymbolStatus` then checks the symbol cache
load_dump would use (`cache\name.pdb\KEY\name.pdb`, two-tier, `.pd_`, `file.ptr`).
Unit tests build synthetic dumps with `DumpAnalysisService.Tests/Minidump/MinidumpBuilder.cs`;
the integration tests triage real CrashScenarios dumps (including unloaded-module,
use-after-free pattern, C++ exception, busy-loop and handle-leak scenarios).
`DumpPathPolicy` enforces `Security:AllowedDumpDirectories` for both triage and `load_dump`.

### Crash Fingerprints (Fingerprinting/)

`CrashFingerprintService` takes the exception code and dump SHA-256 from `IDumpTriageService`
and the faulting stack from `.ecxr;kn 100`, run through
`ISessionManagerService.ExecuteInternalCommandAsync` (service-built commands skip the user
`CdbCommandPolicy`). `CrashFingerprinter` is pure: `ParseStack` reads the kn listing (x64, x86,
inline frames), `NormalizeCallSite` reduces a frame to `module!function` / `module+0xoffset` /
`<unknown>`, `Compute` drops the frames matched by `FingerprintRules` (embedded
`fingerprint-rules.json`, `Fingerprint:RulesPath` replaces it), collapses recursion, hashes the
code plus the first `FrameCount` frames and rates the quality (exact / partial / weak).
`CrashGroupRegistry` keeps groups in a JSON file (`Fingerprint:RegistryPath`, atomic replace,
an unreadable file is set aside) and counts a dump once per fingerprint. Weak fingerprints and
break-instruction (hang) dumps are reported but never recorded. Integration tests fingerprint
the CrashScenarios matrix (same scenario in two processes → same fingerprint, every crash
scenario → a different one).

### Error Handling Pattern

**Exception-based flow** (NOT boolean returns):
```csharp
// Services throw specific exceptions
throw new FileNotFoundException("Dump file not found");
throw new ArgumentException("Session not found");
throw new InvalidOperationException("Session not active");

// Controllers catch and convert to HTTP responses (JobsController.cs)
catch (FileNotFoundException) → 400 Bad Request
catch (ArgumentException) → 404 Not Found
catch (InvalidOperationException) → 400 Bad Request
catch (Exception) → 500 Internal Server Error

// MCP Tools catch and return McpToolResult.Error()
```

## Symbol Resolution Architecture

**Symbol Path Construction** (`Infrastructure/Debugger/SymbolPathBuilder.cs`):

`cache*{cache};{extra paths};{client servers};{DefaultSymbolServers}`:
1. **Cache directive** `cache*{cache}` - applies to every `srv*` entry
2. **Local paths** from `SymbolPathExtra` (client) or `Debugger:DefaultSymbolPathExtra`
3. **Client servers** from `SymbolServers` (tool parameter / `X-Symbol-Servers`): URLs become `srv*{url}`, UNC and local paths are added as is
4. **`Debugger:DefaultSymbolServers`** - added verbatim (so written as `srv*https://...`); when unset, the built-in Microsoft/NuGet list. Setting it replaces that list.

**Symbol Caching & Performance**:
- **Cache-Aware Loading**: Uses `.reload` (NOT `.reload /f`) to leverage cached symbols
- **First dump load**: 10-30 minutes (downloads symbols from Microsoft Symbol Server)
- **Subsequent loads**: 30-60 seconds (uses cached symbols from configured cache directory)
- **Default cache**: `%LOCALAPPDATA%\DumpAnalysisService\symbols`
- **Timeout**: 15 minutes (configurable via `Constants.Debugging.SymbolLoadingTimeoutMinutes`)
- **Smart logging**: Detects cache hits vs downloads and logs appropriately

**Retry Logic** (CdbSessionService.cs): If `.reload` shows `WRONG_SYMBOLS` or `MISSING`, attempts alternative symbol loading strategies with verbose output (`.reload /v`) for diagnostics.

## Dependency Injection Architecture (Program.cs)

**Singleton Services** (shared across all requests):
- `DebuggerConfiguration` - Server-wide debugger settings from appsettings.json
- `IPathExpansionService`, `IPathDetectionService` - Infrastructure
- `IAnalysisService`, `IDiagnosticsService` - Analysis and diagnostics
- `ICdbSessionFactory`, `ISessionManagerService`, `IJobManagerService` - Core services

**Scoped Services** (per HTTP request):
- `ISymbolConfigurationProvider` → `HttpHeaderSymbolConfigurationProvider` - Reads HTTP headers per request

**MCP Server Configuration**:
```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => {
        options.IdleTimeout = TimeSpan.FromHours(1);
        options.Stateless = false; // Enable session state for progress notifications
    })
    .WithToolsFromAssembly(); // Auto-discovers [McpServerTool] attributes
```

## API Architecture - Job-Based Only

**All operations are asynchronous and job-based**. There is NO blocking/synchronous API.

### Job-Based Endpoints (JobsController)
```
POST /api/jobs/load-dump           → 202 Accepted, returns { jobId, statusEndpoint }
POST /api/jobs/execute-command     → 202 Accepted, returns { jobId, statusEndpoint }
POST /api/jobs/basic-analysis      → 202 Accepted, returns { jobId, statusEndpoint }
POST /api/jobs/predefined-analysis → 202 Accepted, returns { jobId, statusEndpoint }
POST /api/jobs/close-session       → 202 Accepted, returns { jobId, statusEndpoint }
POST /api/jobs/triage-dump         → 202 Accepted; job result = DumpTriageResult as JSON
POST /api/jobs/fingerprint-crash   → 202 Accepted; job result = CrashFingerprintResult as JSON
GET  /api/crash-groups             → CrashGroupList (CrashGroupsController)
GET  /api/jobs/{jobId}             → Job status with progress
GET  /api/jobs?state=Running       → List all jobs (with optional state filter)
POST /api/jobs/{jobId}/cancel      → Cancel running job
```

### MCP Tools (DebuggerTools.cs)

All tools follow the same pattern (list_crash_groups, list_jobs and list_analyses answer directly):
1. Create job via `JobManagerService.CreateJob()`
2. Start background `Task.Run()` that calls `SessionManager` with `jobId`
3. Poll job status via `_WaitForJobCompletionAsync()` which reports progress via `IProgress<ProgressNotificationValue>`
4. Return result or throw exception

**Tools**:
- `load_dump` - Load dump file and create session
- `execute_command` - Execute WinDbg/CDB command
- `basic_analysis` - Run comprehensive basic analysis
- `predefined_analysis` - Run specialized analysis (heap, threads, modules, etc.)
- `close_session` - Close session and free resources
- `list_jobs` - List all jobs with status
- `list_analyses` - List available predefined analyses
- `detect_debuggers` - Detect CDB/WinDbg installations
- `triage_dump` - Read a dump's minidump streams without cdb (structured content): classified fault, registers, process traits, threads, modules (unloaded, PDB keys, cache status), memory and handles, kernel bug check summary, hints
- `fingerprint_crash` - Stable fingerprint of the crash in a session (structured content), recorded in the crash group registry
- `list_crash_groups` - Known crash groups, most recently seen first

### Flow:
1. MCP client calls tool → DebuggerTools creates `jobId` immediately
2. Background task starts operation, sends progress via job status updates
3. DebuggerTools polls `JobManagerService.GetJobStatus()` every 1 second
4. MCP client receives progress via `IProgress<ProgressNotificationValue>`
5. When complete, return result or throw exception

**CommandLineClient Flow** (for PowerShell/Azure Functions):
1. Client calls REST API → receives `jobId` (202 Accepted)
2. `SignalRClientService` subscribes to `ProgressHub` for that `jobId`
3. DumpAnalysisService sends progress via SignalR `ProgressHub.SendJobProgress()`
4. Client receives progress callbacks and waits for completion

### MCP Tasks extension

`Program.cs` enables `.WithTasks(new InMemoryMcpTaskStore())` from
`ModelContextProtocol.Extensions.Tasks` (SEP-2663, MCP SDK 2.x). A
task-aware client can run any tool call as a task and poll `tasks/get`;
the SDK owns the task state, while each tool call still creates a job in
`JobManagerService` (visible over REST). The HTTP transport runs in
`HttpServerSessionMode.StatefulForInitializeClients`: clients on protocol
2025-11-25 and older keep a stateful session, 2026-07-28+ clients are
served statelessly.

### Timeout Configuration
- Default: 10 minutes (`Constants.Jobs.DefaultMaxWaitTimeMs`)
- Poll interval: 1 second (`Constants.Jobs.DefaultPollIntervalMs`)
- Configurable in `Shared/Constants.cs`

## Predefined Analysis Types

`AnalysisService` provides 10 analysis types with WinDbg command sequences:
- **basic**: !analyze -v, exception context, thread stacks
- **exception**: Detailed exception record analysis
- **threads**: Thread enumeration with full stacks
- **heap**: Heap statistics and validation
- **modules**: Loaded/unloaded module information
- **handles**: Process handle enumeration
- **locks**: Critical sections and deadlock detection
- **memory**: Virtual memory layout
- **drivers**: Device driver and kernel analysis
- **processes**: Process tree and details

## Common Patterns When Modifying Code

**Adding a new MCP tool**:
1. Add `[McpServerTool]` method to `DebuggerTools.cs`
2. Create job with `_jobManager.CreateJob(JobOperationType.NewOperation)`
3. Start background `Task.Run()` that calls `SessionManager` with `jobId`
4. Use `_WaitForJobCompletionAsync()` to poll and report progress
5. Add `JobOperationType` enum value if needed

**Adding a new analysis type**:
1. Add entry to `AnalysisType` enum (Shared/Models/AnalysisType.cs)
2. Add commands to `AnalysisService._analysisCommands` dictionary
3. Add description to `AnalysisService._analysisDescriptions` dictionary

**Modifying CDB command execution**:
- All CDB commands go through `CdbSessionService._ExecuteCommandInternalAsync()` → `CdbProcessManager.ExecuteCommandAsync()`
- Never bypass the semaphore lock - prevents stdin/stdout corruption
- Use `IProgress<string>` parameter for long-running commands

**Adding symbol configuration source**:
1. Implement `ISymbolConfigurationProvider` interface
2. Register in DI container (Singleton or Scoped)
3. Inject into `DebuggerTools` constructor
4. Update priority chain in `LoadDump` tool
