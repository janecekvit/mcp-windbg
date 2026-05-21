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

```
┌─────────────┐
│ Claude Code │  MCP Streamable HTTP
│ (MCP Client)│
└──────┬──────┘
       │ http://localhost:7997/mcp
       ↓
┌──────────────────────────────────────────────────┐
│        DumpAnalysisService (port 7997)           │
│                                                  │
│  ┌──────────────┐      ┌───────────────────┐   │
│  │ MCP HTTP     │      │ REST API          │   │
│  │ /mcp/*       │      │ /api/jobs/*       │   │
│  │ (DebuggerTools)     │ (JobsController)  │   │
│  └──────┬───────┘      └────────┬──────────┘   │
│         │                       │               │
│         └───────────┬───────────┘               │
│                     ↓                           │
│         ┌──────────────────────┐                │
│         │ SessionManager       │                │
│         │ + JobManager         │                │
│         │ + ProgressHub        │                │
│         │ (SignalR)            │                │
│         └──────────┬───────────┘                │
│                    │                            │
└────────────────────┼────────────────────────────┘
                     ↓
              ┌─────────────┐
              │ CDB Process │
              │ (per dump)  │
              └─────────────┘

┌────────────────┐
│ PowerShell /   │  REST API (Invoke-RestMethod)
│ Azure Function │
└────────┬───────┘
         │ http://localhost:7997/api
         ↓
    (DumpAnalysisService)

┌──────────────────┐
│ CommandLineClient│  REST API + SignalR
└────────┬─────────┘
         │ http://localhost:7997
         ↓
    (DumpAnalysisService)
```

### Projects Structure

1. **DumpAnalysisService** - All-in-One: MCP Server + REST API + Debugging Engine
   - Entry Point: `Program.cs` - ASP.NET Core Web API (port 7997)
   - **MCP Integration**: ModelContextProtocol.AspNetCore SDK
     - HTTP Transport: `/mcp/sse` (GET), `/mcp/messages` (POST)
     - Tools: `DebuggerTools.cs` with 8 [McpServerTool] methods
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
   - `DumpAnalysisService.Tests`: Service-layer unit tests for `DumpAnalysisService` (session manager, job manager, analysis service, controllers).
   - `Shared.Tests`: Unit tests for the `Shared` project (configuration providers, client libraries, constants). Some SignalR-dependent tests are skipped under xUnit when no host is running.
   - `DumpAnalysisService.IntegrationTests`: Integration suite that boots `DumpAnalysisService` in-process via `WebApplicationFactory` and exercises the REST + SignalR flow against a real `TestCrasher` dump.
   - `DumpAnalysisService.TestCrasher`: Tiny console app that deliberately faults to produce a dump file consumed by the integration tests (copied next to the test output via a `CopyTestCrasher` MSBuild target).

### Docker Distribution

Pre-built Windows container images are published to **`ghcr.io/janecekvit/mcp-windbg`** on every `v*.*.*` release tag (workflow: `.github/workflows/build-and-release.yml`).

- **Base image:** `mcr.microsoft.com/windows/servercore:ltsc2022` (Linux containers cannot run `cdb.exe`).
- **Debugging tools:** installed during image build via the Windows SDK web setup (`fwlink/?linkid=2237387 /features OptionId.WindowsDesktopDebuggers`).
- **Build input:** the existing `publish/win-x64/` self-contained publish output — Docker layer simply `COPY`s the same artefacts the Release ZIP ships. No second build path.
- **Health probe:** `HEALTHCHECK` polls `GET /api/jobs` (returns `200` with an empty array once the service is ready). The dedicated `/api/diagnostics/health` route exists but is not currently the probe target.
- **CI gate:** Docker build/push steps only run on tag refs (`startsWith(github.ref, 'refs/tags/v')`); branch pushes skip Docker entirely.

See `Dockerfile`, `.dockerignore`, and the README section "Running via Docker" for the user-facing flow.

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
// Command: !analyze -v; .echo __END_COMMAND_abc123__
// Read stdout until marker found, timeout after 5 minutes
// Progress reporting every 2 seconds for long commands
```

**Why markers?** CDB output is continuous and unpredictable. Markers provide reliable command completion detection.

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
  → Store in _sessions dictionary

ExecuteCommandAsync() (CdbSessionService.cs)
  → Acquire semaphore lock
  → Write command + marker to stdin
  → Read stdout until marker (with timeout & progress)
  → Release semaphore

CancelSessionAsync() (SessionManagerService.cs)
  → Call session.CancelAsync()
  → Send "q" to CDB stdin
  → WaitForExit(5s) or Kill()
  → Remove from dictionary, Dispose()
```

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

**Symbol Path Construction** (CdbSessionService.cs:70-109):

Symbol path is built from configuration with the following priority:
1. **Local paths** from `SymbolPathExtra` - Direct file paths (highest priority)
2. **Custom servers** from `SymbolServers` - Smart formatted with cache
3. **Default Microsoft servers** - Always included (lowest priority)

**Symbol Server Formatting**:
- URLs: `srv*{cache}*{url}` → `srv*C:\symbols*https://msdl.microsoft.com/download/symbols`
- UNC paths: Added directly → `\\server\symbols`
- Local paths: Added directly → `C:\MySymbols`

**Symbol Caching & Performance**:
- **Cache-Aware Loading**: Uses `.reload` (NOT `.reload /f`) to leverage cached symbols
- **First dump load**: 10-30 minutes (downloads symbols from Microsoft Symbol Server)
- **Subsequent loads**: 30-60 seconds (uses cached symbols from configured cache directory)
- **Default cache**: `%LOCALAPPDATA%\CdbMcpServer\symbols`
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
GET  /api/jobs/{jobId}             → Job status with progress
GET  /api/jobs?state=Running       → List all jobs (with optional state filter)
POST /api/jobs/{jobId}/cancel      → Cancel running job
```

### MCP Tools (DebuggerTools.cs)

All 8 tools follow the same pattern:
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

### MCP-Native Tasks (Adapter)

The MCP layer also exposes the standard `tasks/list`, `tasks/get`,
`tasks/result`, and `tasks/cancel` protocol methods via
`JobManagerBackedTaskStore` (`IMcpTaskStore`). One MCP TaskId
maps 1:1 to one JobId in `JobManagerService` — the underlying
state model is shared with the REST API. Clients can choose
streaming (`IProgress<ProgressNotificationValue>` notifications)
or polling (`tasks/get`); both paths read the same job state.

`IMcpTaskStore` is marked experimental in SDK 1.3.0 (diagnostic
`MCPEXP001`). The adapter pattern keeps the experimental surface
to a single file
(`DumpAnalysisService/Tasks/JobManagerBackedTaskStore.cs`).

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
- All CDB commands go through `CdbSessionService.ExecuteCommandInternalAsync()`
- Never bypass the semaphore lock - prevents stdin/stdout corruption
- Use `IProgress<string>` parameter for long-running commands

**Adding symbol configuration source**:
1. Implement `ISymbolConfigurationProvider` interface
2. Register in DI container (Singleton or Scoped)
3. Inject into `DebuggerTools` constructor
4. Update priority chain in `LoadDump` tool
