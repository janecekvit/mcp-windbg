# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repository contains a modern C# MCP (Model Context Protocol) server for interactive Windows memory dump analysis using Microsoft's Command Line Debugger (cdb.exe) or WinDbg. The system uses a **dual-process architecture** where an MCP protocol layer communicates with a separate HTTP API service that manages long-running CDB debugging sessions.

## Build and Development Commands

```powershell
# Build single-file executables (recommended for distribution)
.\Scripts\Publish.ps1

# Development builds
dotnet build

# Run tests (unit tests for both projects)
dotnet test
dotnet test --filter "FullyQualifiedName~AnalysisService"  # Run specific test class

# Development: Run both services
# Terminal 1: Start background service
dotnet run --project BackgroundService
# Terminal 2: Start MCP proxy
dotnet run --project McpProxy

# Run background service on custom port
dotnet run --project BackgroundService -- 8080
```

## Architecture Overview - Why Dual Process + Job-Based System?

**Problem**: CDB dump loading and symbol resolution can take **several minutes**. MCP clients expect immediate responses and need progress updates.

**Solution**: Two separate processes communicating via HTTP + Job-based async operations with SignalR for real-time progress:

```
┌─────────────┐           ┌──────────────┐           ┌─────────────────┐
│ MCP Client  │◄─────────►│  McpProxy    │◄─ HTTP ──►│BackgroundService│
│  (Claude)   │  JSON-RPC │ (stdin/MCP)  │           │  (ASP.NET API)  │
└─────────────┘           └──────────────┘           └─────────────────┘
                                │                             │
                                │    WebSocket (SignalR)      │
                                │◄────────────────────────────┤
                                │  Real-time Progress Updates │
                                                              │
                                                              ▼
                                                      ┌─────────────────┐
                                                      │ JobManager      │
                                                      │ + ProgressHub   │
                                                      └────────┬────────┘
                                                              │
                                                              ▼
                                                       ┌─────────────┐
                                                       │ CDB Process │
                                                       │  (per dump) │
                                                       └─────────────┘
```

### 1. McpProxy (MCP Protocol Layer)
**Entry Point**: `McpProxy/Program.cs` → `McpProxy.cs:23`
**Communication**: stdin/stdout JSON-RPC ↔ HTTP + SignalR to BackgroundService

**Key Services**:
- `CommunicationService`: Handles MCP JSON-RPC protocol over stdio
- `DebuggerApiService`: HTTP client that calls BackgroundService job-based endpoints
- `SignalRClientService`: WebSocket client for real-time progress notifications
- `ToolsService`: Registers 8 MCP tools (load_dump, execute_command, basic_analysis, predefined_analysis, close_session, list_jobs, list_analyses, detect_debuggers)

**Why Job-Based + SignalR?**
- **Non-blocking**: Operations return jobId immediately, no hanging
- **Real-time progress**: SignalR pushes updates (0-100%) to MCP client
- **Polling fallback**: If SignalR fails, falls back to HTTP polling every 1 second
- **Timeout handling**: Configurable timeout (default 10 minutes) via `Constants.Jobs`
- BackgroundService can run independently for debugging

### 2. BackgroundService (Debugging Engine + Job Management)
**Entry Point**: `BackgroundService/Program.cs` - ASP.NET Core Web API
**Default Port**: 8080 (configurable via args or env var)

**Key Services**:
- `SessionManagerService`: Thread-safe session orchestration using `ConcurrentDictionary<string, ICdbSessionService>` - **All methods require jobId for progress tracking**
- `JobManagerService`: Thread-safe job tracking using `ConcurrentDictionary<string, JobStatus>` with auto-cleanup (every 10 min)
- `CdbSessionService`: **One CDB process per session** - manages stdin/stdout communication
- `AnalysisService`: Predefined WinDbg command sequences (basic, heap, threads, etc.)
- `PathDetectionService`: Auto-detects CDB.exe from Windows SDK/WinDbg installations

**SignalR Hub**:
- `ProgressHub`: WebSocket hub at `/hubs/progress` for real-time progress notifications

**Controllers**:
- `JobsController`: **Job-based async API** at `/api/jobs/*` (primary API)
- `DiagnosticsController`: Health checks and debugger detection

### 3. Shared Library
**Purpose**: Shared models, contracts, and constants between both processes

**Key Files**:
- `ApiContracts.cs`: Request/Response models, API endpoints
- `Constants.cs`: Network ports, timeouts, MCP error codes
- `OperationResult.cs`: Result monad for error handling

## Critical Implementation Details

### Asynchronous Command Execution (CdbSessionService.cs:297-400)

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
CreateSessionWithDumpAsync() (SessionManagerService.cs:61)
  → Generate 8-char session ID
  → CdbSessionService.LoadDumpAsync() (CdbSessionService.cs:42)
    → Process.Start("cdb.exe -z dump.dmp")
    → InitializeSessionAsync() - symbol setup (CdbSessionService.cs:153)
      → Configure symbol options, add symbol servers
      → .reload /f with retry logic for symbol failures
  → Store in _sessions dictionary

ExecuteCommandAsync() (CdbSessionService.cs:263)
  → Acquire semaphore lock
  → Write command + marker to stdin
  → Read stdout until marker (with timeout & progress)
  → Release semaphore

CancelSessionAsync() (SessionManagerService.cs:208)
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

// MCP layer converts to McpToolResult.Error() (McpProxy.cs:47-51)
```

### Factory Patterns for MCP Models

All MCP response objects use static factory methods:
```csharp
McpToolResult.Success(text)  // Success response
McpToolResult.Error(message) // Error response
McpResponse.Success(id, result)
McpResponse.NotInitialized(id)
McpError.ServerNotInitialized()
```

## Symbol Resolution Architecture

**Symbol Path Priority** (CdbSessionService.cs:70-109):
1. `SYMBOL_PATH_EXTRA` - Direct local paths (highest priority)
2. `SYMBOL_SERVERS` - Custom symbol servers (smart formatting)
3. Default Microsoft servers (lowest priority)

**Symbol Server Formatting**:
- URLs: `srv*{cache}*{url}` → `srv*C:\symbols*https://msdl.microsoft.com/download/symbols`
- UNC paths: Added directly → `\\server\symbols`
- Local paths: Added directly → `C:\MySymbols`

**Symbol Caching & Performance**:
- **Cache-Aware Loading**: Uses `.reload` (NOT `.reload /f`) to leverage cached symbols
- **First dump load**: 10-30 minutes (downloads symbols from Microsoft Symbol Server)
- **Subsequent loads**: 30-60 seconds (uses cached symbols from `SYMBOL_CACHE`)
- **Default cache**: `%LOCALAPPDATA%\CdbMcpServer\symbols`
- **Timeout**: 15 minutes (configurable via `Constants.Debugging.SymbolLoadingTimeoutMinutes`)
- **Smart logging**: Detects cache hits vs downloads and logs appropriately

**Retry Logic** (CdbSessionService.cs:239): If `.reload` shows `WRONG_SYMBOLS` or `MISSING`, attempts alternative symbol loading strategies with verbose output (`.reload /v`) for diagnostics.

## Configuration Strategy

**Priority Order**:
1. `appsettings.json` (recommended for deployment)
2. Environment variables (fallback, useful for CI/CD)
3. Auto-detection (CDB path) or defaults (symbol cache)

**Key Environment Variables**:
```bash
CDB_PATH               # Override auto-detected debugger
SYMBOL_CACHE           # Default: %LOCALAPPDATA%\CdbMcpServer\symbols
SYMBOL_PATH_EXTRA      # Additional local symbol directories
SYMBOL_SERVERS         # Custom symbol servers (;-separated)
BACKGROUND_SERVICE_URL # Default: http://localhost:8080
```

## Testing Architecture

**Test Projects**:
- `BackgroundService.Tests`: Service layer unit tests
- `McpProxy.Tests`: MCP protocol and API client tests

**Test Patterns**:
- Mocked dependencies using interfaces (`ISessionManagerService`, etc.)
- Parameterized tests for analysis types
- Integration tests for HTTP endpoints

## Code Organization Principles

**Dependency Injection**: Both projects use Microsoft.Extensions.DependencyInjection
- McpProxy: Scoped services for request handling (Program.cs:16-21)
- BackgroundService: Singleton for session management (Program.cs:15-17)

**Interface Segregation**: Every service has an interface for testability
- `ISessionManagerService`, `ICdbSessionService`, `IAnalysisService`, etc.

**Logging**: Structured logging to stderr for MCP compatibility
- McpProxy: `LogToStandardErrorThreshold = LogLevel.Trace` (Program.cs:28)
- BackgroundService: Console logging only

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

### Flow:
1. Client calls POST endpoint → receives `jobId` immediately (202 Accepted)
2. McpProxy subscribes to SignalR `ProgressHub` for that `jobId`
3. BackgroundService starts operation in `Task.Run()`, sends progress via SignalR
4. SignalRClientService receives progress, forwards to MCP client via notifications
5. McpProxy also polls `GET /api/jobs/{jobId}` every 1 second as fallback
6. When complete, SignalR sends `Completed` notification
7. McpProxy unsubscribes and returns final result to MCP client

### Timeout Configuration
- Default: 10 minutes (`Constants.Jobs.DefaultMaxWaitTimeMs`)
- Poll interval: 1 second (`Constants.Jobs.DefaultPollIntervalMs`)
- Configurable in `Shared/Constants.cs`

## Common Patterns When Modifying Code

**Adding a new MCP tool**:
1. Add job-based endpoint to `JobsController` (returns 202 + jobId)
2. Create background Task.Run() that calls `SessionManager` with jobId
3. Add method to `IDebuggerApiService` interface
4. Implement HTTP call + SignalR subscription in `DebuggerApiService`
5. Add tool registration in `ToolsService`
6. Add switch case in `McpProxy.HandleToolCallAsync()`
7. Add constant to `Constants.McpToolNames`
8. Add `JobOperationType` enum value if needed

**Adding a new analysis type**:
1. Add entry to `AnalysisType` enum (Shared/Models/AnalysisType.cs)
2. Add commands to `AnalysisService._analysisCommands` dictionary
3. Add description to `AnalysisService._analysisDescriptions` dictionary

**Modifying CDB command execution**:
- All CDB commands go through `CdbSessionService.ExecuteCommandInternalAsync()`
- Never bypass the semaphore lock - prevents stdin/stdout corruption
- Use `IProgress<string>` parameter for long-running commands