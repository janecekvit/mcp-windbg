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

## Architecture Overview - Why Dual Process?

**Problem**: CDB dump loading and symbol resolution can take **several minutes**. MCP clients expect immediate responses and need progress updates.

**Solution**: Two separate processes communicating via HTTP:

```
┌─────────────┐           ┌──────────────┐           ┌─────────────────┐
│ MCP Client  │◄─────────►│  McpProxy    │◄─ HTTP ──►│BackgroundService│
│  (Claude)   │  JSON-RPC │ (stdin/MCP)  │           │  (ASP.NET API)  │
└─────────────┘           └──────────────┘           └─────────────────┘
                          Progress notifications              │
                          over stdout                         ▼
                                                       ┌─────────────┐
                                                       │ CDB Process │
                                                       │  (per dump) │
                                                       └─────────────┘
```

### 1. McpProxy (MCP Protocol Layer)
**Entry Point**: `McpProxy/Program.cs` → `McpProxy.cs:23`
**Communication**: stdin/stdout JSON-RPC ↔ HTTP client to BackgroundService

**Key Services**:
- `CommunicationService`: Handles MCP JSON-RPC protocol over stdio
- `DebuggerApiService`: HTTP client that calls BackgroundService endpoints
- `ToolsService`: Registers 8 MCP tools (load_dump, execute_command, etc.)

**Why HTTP instead of direct CDB access?**
- Decouples MCP protocol from slow CDB operations
- Allows async/await for long-running commands
- BackgroundService can run independently for debugging
- Progress reporting via MCP notifications while waiting

### 2. BackgroundService (Debugging Engine)
**Entry Point**: `BackgroundService/Program.cs` - ASP.NET Core Web API
**Default Port**: 8080 (configurable via args or env var)

**Key Services**:
- `SessionManagerService`: Thread-safe session orchestration using `ConcurrentDictionary<string, ICdbSessionService>`
- `CdbSessionService`: **One CDB process per session** - manages stdin/stdout communication
- `AnalysisService`: Predefined WinDbg command sequences (basic, heap, threads, etc.)
- `PathDetectionService`: Auto-detects CDB.exe from Windows SDK/WinDbg installations

**Controllers**:
- `SessionsController`: REST endpoints at `/api/sessions/*`
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

CloseSession() (SessionManagerService.cs:160)
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

// Controllers catch and convert to HTTP responses (SessionsController.cs:32-57)
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

**Retry Logic** (CdbSessionService.cs:226): If `.reload /f` shows `WRONG_SYMBOLS` or `MISSING`, attempts alternative symbol loading strategies.

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

## Common Patterns When Modifying Code

**Adding a new MCP tool**:
1. Add endpoint to `SessionsController` or `DiagnosticsController`
2. Add method to `IDebuggerApiService` interface
3. Implement HTTP call in `DebuggerApiService`
4. Add tool registration in `ToolsService`
5. Add switch case in `McpProxy.HandleToolCallAsync()`
6. Add constant to `Constants.McpToolNames`

**Adding a new analysis type**:
1. Add entry to `AnalysisType` enum (Shared/Models/AnalysisType.cs)
2. Add commands to `AnalysisService._analysisCommands` dictionary
3. Add description to `AnalysisService._analysisDescriptions` dictionary

**Modifying CDB command execution**:
- All CDB commands go through `CdbSessionService.ExecuteCommandInternalAsync()`
- Never bypass the semaphore lock - prevents stdin/stdout corruption
- Use `IProgress<string>` parameter for long-running commands