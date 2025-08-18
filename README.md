# CDB MCP Server

MCP (Model Context Protocol) server for interactive debugging of Windows memory dump files using Microsoft Command Line Debugger (cdb.exe).

## Features

- **Interactive debugging**: Persistent CDB sessions for each dump file
- **Predefined analyses**: 10 types of specialized analyses (basic, exception, threads, heap, etc.)
- **Custom commands**: Ability to execute custom WinDbg/CDB commands
- **Session management**: Concurrent work with multiple dump files
- **MCP compatibility**: Standard MCP protocol for integration

## Requirements

- .NET 8.0+
- Windows SDK Debuggers (cdb.exe)
- Access to Microsoft symbol server (for downloading symbols)

## Installation

### Quick Installation (Single-file executable)
```powershell
# Build single-file executable
.\Scripts\Publish.ps1

# Run
.\publish\McpProxy.exe
```

### Development build
```bash
dotnet build
dotnet run
```

## Automatic Debugger Detection

Server automatically detects available CDB/WinDbg installations:

- **Windows SDK** (preferred): `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe`
- **WinDbg Store App**: `C:\Program Files\WindowsApps\Microsoft.WinDbg_*\amd64\windbg.exe`
- **Various architectures**: x64, x86, amd64

Use the `detect_debuggers` tool to discover available installations.

## Configuration

Server is configured using environment variables (optional):

- `CDB_PATH`: Custom path to cdb.exe/windbg.exe (overrides auto-detection)
- `SYMBOL_CACHE`: Local cache for symbols (default: `%LOCALAPPDATA%\CdbMcpServer\symbols`)
- `SYMBOL_PATH_EXTRA`: Additional symbol paths

## MCP Tools

### load_dump
Loads a memory dump and creates a new debugging session.

**Parameters:**
- `dump_file_path`: Path to .dmp file

### execute_command
Executes a WinDbg/CDB command in an existing session.

**Parameters:**
- `session_id`: Debugging session ID
- `command`: Command to execute (e.g., "kb", "!analyze -v")

### predefined_analysis
Runs a predefined analysis.

**Parameters:**
- `session_id`: Debugging session ID
- `analysis_type`: Analysis type (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)

### basic_analysis
Runs basic analysis (equivalent to PowerShell script).

**Parameters:**
- `session_id`: Debugging session ID

### list_sessions
Lists all active debugging sessions.

### list_analyses
Lists all available predefined analyses with descriptions.

### detect_debuggers
Detects available CDB/WinDbg installations on the system.

### close_session
Closes a debugging session and releases resources.

**Parameters:**
- `session_id`: ID of debugging session to close

## Predefined Analyses

1. **basic** - Complete basic analysis (exception context, analyze -v, thread stacks)
2. **exception** - Detailed exception analysis with exception and context records
3. **threads** - Complete thread analysis including information and stacks
4. **heap** - Heap analysis including statistics and validation
5. **modules** - Module analysis (loaded, detailed, unloaded)
6. **handles** - Handle analysis including process handles
7. **locks** - Critical sections analysis and deadlock detection
8. **memory** - Virtual memory and address space analysis
9. **drivers** - Driver and device objects analysis
10. **processes** - Process analysis and process tree

## Claude Code Integration

### 1. Build and Installation
```powershell
# Build single-file executable
.\Scripts\Publish.ps1
# or
dotnet publish McpProxy\McpProxy.csproj -c Release -r win-x64 -o publish --self-contained true -p:PublishSingleFile=true
```

### 2. Claude Code Configuration

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcp": {
    "servers": {
      "cdb-debugging": {
        "command": "D:\\Git\\mcp-windbg\\publish\\McpProxy.exe",
        "args": []
      }
    }
  }
}
```

### 3. Usage in Claude Code

After restarting Claude Code, you can use:

- `detect_debuggers` - verify debugger configuration
- `load_dump` - load dump file and create session
- `basic_analysis` - complete crash analysis
- `execute_command` - custom CDB commands
- `predefined_analysis` - specialized analyses (heap, threads, modules, etc.)

**Example workflow:**
1. "Use detect_debuggers to verify configuration"
2. "Load dump file D:\\crash.dmp using load_dump"
3. "Perform basic_analysis on session"
4. "Run predefined_analysis of type heap"

## Direct MCP Usage

```json
// Load dump
{"method": "tools/call", "params": {"name": "load_dump", "arguments": {"dump_file_path": "C:\\dumps\\crash.dmp"}}}

// Run basic analysis
{"method": "tools/call", "params": {"name": "basic_analysis", "arguments": {"session_id": "abc12345"}}}

// Custom command
{"method": "tools/call", "params": {"name": "execute_command", "arguments": {"session_id": "abc12345", "command": "!heap -s"}}}

// Specialized analysis
{"method": "tools/call", "params": {"name": "predefined_analysis", "arguments": {"session_id": "abc12345", "analysis_type": "heap"}}}
```