# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repository contains a modern C# MCP (Model Context Protocol) server for interactive Windows memory dump analysis using Microsoft's Command Line Debugger (cdb.exe) or WinDbg. The system provides both an MCP-compatible interface and a background service for debugging session management.

## Build and Development Commands

```powershell
# Build single-file executables (recommended for distribution)
.\Scripts\Publish.ps1

# Development builds
dotnet build
dotnet run --project McpProxy
dotnet run --project BackgroundService

# Run background service on specific port
dotnet run --project BackgroundService 8080
```

## Architecture Overview

The project uses a **dual-service architecture** with clear separation of concerns:

### 1. McpProxy (MCP Protocol Layer)
- **Entry Point**: `McpProxy.exe` - Main MCP server executable
- **Responsibility**: Handles MCP protocol communication, tool registration, and client interactions
- **Key Components**:
  - `ICommunicationService`: Manages JSON-RPC communication over stdin/stdout
  - `IToolsService`: Registers and routes MCP tool calls
  - `IDebuggerApiService`: HTTP client for communicating with BackgroundService
  - `INotificationService`: Sends progress notifications to MCP clients

### 2. BackgroundService (Debugging Engine)
- **Entry Point**: `BackgroundService.exe` - HTTP API server for debugging operations
- **Responsibility**: Manages CDB processes, debugging sessions, analysis execution, and background task management
- **Key Components**:
  - `ISessionManagerService`: Orchestrates debugging sessions and their lifecycle
  - `ICdbSessionService`: Direct interface to individual CDB processes
  - `IAnalysisService`: Provides predefined analysis commands and descriptions
  - `IPathDetectionService`: Auto-detects available CDB/WinDbg installations
  - `IBackgroundTaskService`: Manages long-running background operations with progress tracking

### 3. Data Flow Architecture

**Synchronous Operations:**
```
MCP Client → McpProxy → HTTP API → BackgroundService → CDB Process
```

**Asynchronous Task Operations:**
```
MCP Client → McpProxy → Async Task API → BackgroundService (persistent)
           ↘ Task ID returned immediately

Monitor: MCP Client → McpProxy → Task Status API → BackgroundService
```

**Exception-Based Error Handling**: The codebase uses modern exception-based error handling instead of boolean return patterns. Services throw specific exceptions (`ArgumentException`, `FileNotFoundException`, `InvalidOperationException`) which are caught and converted to appropriate HTTP responses or MCP errors.

**Factory Patterns for Models**: MCP model classes (`McpResponse`, `McpToolResult`, `McpError`) use factory methods for clean object creation:
- `McpToolResult.Success(text)` / `McpToolResult.Error(message)`
- `McpResponse.Success(id, result)` / `McpResponse.NotInitialized(id)`
- `McpError.ServerNotInitialized()` / `McpError.Custom(code, message)`

## Session Management

The system maintains **persistent CDB sessions** for each loaded dump file:
- Each session gets a unique 8-character ID
- Sessions run independent CDB processes with dedicated stdin/stdout
- Automatic cleanup on disposal or application shutdown
- Concurrent session support for multiple dump files

## Asynchronous Task Management

The system includes comprehensive **asynchronous task support** to handle MCP's request-response architecture:

### Key Features:
- **Fire-and-forget execution**: Long-running operations continue after MCP client disconnects
- **Progress tracking**: Automatic logging every 30 seconds with task status updates
- **Task lifecycle management**: Running → Completed/Failed/Cancelled state transitions
- **Concurrent execution**: Multiple asynchronous tasks can run simultaneously
- **Persistent results**: Task results remain available until server restart

### Task Types:
- **LoadDump**: Asynchronous dump file loading with symbol resolution
- **BasicAnalysis**: Comprehensive dump analysis asynchronously
- **PredefinedAnalysis**: Specific analysis types (threads, heap, modules, etc.)
- **ExecuteCommand**: Long-running CDB commands asynchronously

### API Endpoints:
- `POST /api/tasks/{operation}` - Start asynchronous task, returns task ID
- `GET /api/tasks/{taskId}` - Get task status and results
- `GET /api/tasks` - List all background tasks
- `DELETE /api/tasks/{taskId}` - Cancel running task

## Symbol Resolution Strategy

- **Primary**: Microsoft public symbol server with local caching
- **Auto-detection**: Scans Windows SDK and Store App installations
- **Configurable**: Environment variables override auto-detection
- **Cache Management**: Automatic symbol cache directory creation

## Environment Configuration

```bash
CDB_PATH         # Override auto-detected debugger path
SYMBOL_CACHE     # Custom symbol cache location (default: %LOCALAPPDATA%\CdbMcpServer\symbols)
SYMBOL_PATH_EXTRA # Additional symbol paths
BACKGROUND_SERVICE_URL # McpProxy → BackgroundService communication (default: http://localhost:8080)
```

## MCP Tools and Background Support

All core debugging MCP tools support both **synchronous** and **asynchronous** execution modes:

### Core Tools with Async Support:
- `load_dump` - Add `"async": true` parameter for asynchronous execution
- `execute_command` - Add `"async": true` parameter for asynchronous execution
- `basic_analysis` - Add `"async": true` parameter for asynchronous execution
- `predefined_analysis` - Add `"async": true` parameter for asynchronous execution

### Asynchronous Task Management Tools:
- `get_task_status` - Monitor asynchronous task progress and results
- `list_background_tasks` - List all asynchronous tasks with status
- `cancel_task` - Cancel running asynchronous tasks

### Usage Pattern:
```json
// Start asynchronous task
{"dump_file_path": "C:\\dump.dmp", "async": true}
// Returns: {"taskId": "abc12345", "message": "Asynchronous task started"}

// Monitor progress
{"task_id": "abc12345"}
// Returns: {"status": "Running", "description": "Loading dump...", "startedAt": "..."}
```

## Predefined Analysis Types

The `AnalysisService` provides 10 predefined analysis types with corresponding WinDbg command sequences:
- **basic**: Complete crash analysis (!analyze -v, stacks, exception context)
- **exception**: Detailed exception record analysis
- **threads**: Thread enumeration and stack traces
- **heap**: Heap statistics and validation
- **modules**: Loaded/unloaded module analysis
- **handles**: Process handle enumeration
- **locks**: Critical section and deadlock detection
- **memory**: Virtual memory layout analysis
- **drivers**: Device driver analysis
- **processes**: Process tree and detailed process information

## Error Handling Patterns

When modifying services, follow the established exception-based patterns:
- Use specific exception types (`ArgumentException`, `FileNotFoundException`, `InvalidOperationException`)
- Log errors before throwing exceptions
- HTTP endpoints catch exceptions and return appropriate status codes
- MCP layer converts exceptions to `McpToolResult.Error()` responses

## Dependencies

- **.NET 8.0**: Runtime and development framework
- **Windows SDK Debuggers**: CDB.exe for dump analysis
- **Microsoft Symbol Server**: Internet connectivity for symbol downloads