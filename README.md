# WinDbg MCP Server

MCP (Model Context Protocol) server for interactive debugging of Windows memory dump files using Microsoft Command Line Debugger (cdb.exe) or WinDbg.

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

The server supports multiple configuration methods with the following priority order:
1. **appsettings.json** (recommended)
2. **Environment variables** (fallback)
3. **Default values**

### Configuration Files

#### McpProxy Configuration
Create `McpProxy/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "BackgroundService": {
    "BaseUrl": "http://localhost:8080"
  }
}
```

#### BackgroundService Configuration
Create `BackgroundService/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Debugger": {
    "CdbPath": null,
    "SymbolCache": null,
    "SymbolPathExtra": "",
    "SymbolServers": null
  }
}
```

#### Symbol Configuration Examples

**Example 1: Company with internal symbol server**
```json
{
  "Debugger": {
    "SymbolCache": "D:\\SymbolCache",
    "SymbolPathExtra": "C:\\MyProject\\Debug",
    "SymbolServers": "https://symbols.company.com"
  }
}
```
Results in symbol path:
```
C:\MyProject\Debug;srv*D:\SymbolCache*https://symbols.company.com;srv*D:\SymbolCache*https://msdl.microsoft.com/download/symbols
```

**Example 2: Mixed local and network symbols**
```json
{
  "Debugger": {
    "SymbolServers": "\\\\buildserver\\symbols;https://private-symbols.com"
  }
}
```

**Example 3: Local development only**
```json
{
  "Debugger": {
    "SymbolPathExtra": "C:\\MyPDBs;D:\\ThirdParty\\Symbols"
  }
}
```

### Environment Variables (Optional Fallback)

- `CDB_PATH`: Custom path to cdb.exe/windbg.exe (overrides auto-detection)
- `BACKGROUND_SERVICE_URL`: Background service endpoint (default: `http://localhost:8080`)

#### Symbol Configuration Parameters

Understanding the different symbol parameters:

- **`SYMBOL_CACHE`**: 📦 **Local download directory**
  - Where downloaded symbols are stored permanently
  - Default: `%LOCALAPPDATA%\CdbMcpServer\symbols`
  - Used by all `srv*cache*server` entries

- **`SYMBOL_PATH_EXTRA`**: 📁 **Direct local paths**
  - Raw file paths added directly to symbol path
  - For local PDB directories: `C:\MyPDBs;D:\ProjectSymbols`
  - No automatic formatting - added as-is

- **`SYMBOL_SERVERS`**: 🌐 **Remote symbol servers**
  - Smart formatting for URLs and network paths
  - URLs become: `srv*{cache}*{url}`
  - File paths become: direct paths
  - Example: `https://symbols.company.com;\\server\symbols`

#### Symbol Path Priority Order
1. **SYMBOL_PATH_EXTRA** (highest priority - local PDBs)
2. **SYMBOL_SERVERS** (custom remote servers)
3. **Default Microsoft servers** (lowest priority)

#### Quick Reference Table

| Parameter | Purpose | Example | Result Format |
|-----------|---------|---------|---------------|
| `SYMBOL_CACHE` | Download storage | `D:\Symbols` | Used as cache in `srv*` entries |
| `SYMBOL_PATH_EXTRA` | Local PDB folders | `C:\MyPDBs` | Added as-is: `C:\MyPDBs` |
| `SYMBOL_SERVERS` | Remote servers | `https://srv.com` | Smart format: `srv*cache*https://srv.com` |

## MCP Tools

All debugging tools support both **synchronous** and **asynchronous** execution modes:

- **Synchronous mode** (default): Returns results immediately, may timeout on long operations
- **Asynchronous mode**: Returns task ID immediately, allows monitoring via task management tools

### Core Debugging Tools

### load_dump
Loads a memory dump and creates a new debugging session.

**Parameters:**
- `dump_file_path`: Path to .dmp file
- `async` (optional): Set to `true` for asynchronous execution

**Asynchronous mode returns:** Task ID for monitoring progress

### execute_command
Executes a WinDbg/CDB command in an existing session.

**Parameters:**
- `session_id`: Debugging session ID
- `command`: Command to execute (e.g., "kb", "!analyze -v")
- `async` (optional): Set to `true` for asynchronous execution

**Asynchronous mode returns:** Task ID for monitoring progress

### predefined_analysis
Runs a predefined analysis.

**Parameters:**
- `session_id`: Debugging session ID
- `analysis_type`: Analysis type (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)
- `async` (optional): Set to `true` for asynchronous execution

**Asynchronous mode returns:** Task ID for monitoring progress

### basic_analysis
Runs basic analysis (equivalent to PowerShell script).

**Parameters:**
- `session_id`: Debugging session ID
- `async` (optional): Set to `true` for asynchronous execution

**Asynchronous mode returns:** Task ID for monitoring progress

### Session Management Tools

### list_sessions
Lists all active debugging sessions.

### close_session
Closes a debugging session and releases resources.

**Parameters:**
- `session_id`: ID of debugging session to close

### Asynchronous Task Management Tools

### get_task_status
Gets the status and results of an asynchronous task.

**Parameters:**
- `task_id`: Asynchronous task ID

**Returns:** Task status (Running, Completed, Failed, Cancelled), progress, and results

### list_background_tasks
Lists all asynchronous tasks with their current status.

**Returns:** All asynchronous tasks with status, start time, completion time, and session info

### cancel_task
Cancels a running asynchronous task.

**Parameters:**
- `task_id`: Asynchronous task ID to cancel

### System Information Tools

### list_analyses
Lists all available predefined analyses with descriptions.

### detect_debuggers
Detects available CDB/WinDbg installations on the system.

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

## Asynchronous Task Workflow

Asynchronous execution is ideal for long-running operations that might timeout in synchronous mode:

### Starting an Asynchronous Task
```json
{
  "dump_file_path": "C:\\dumps\\app.dmp",
  "async": true
}
```
**Returns:** `Task ID: abc12345`

### Monitoring Progress
Use the task ID to check status:
```json
{
  "task_id": "abc12345"
}
```

**Task Status Values:**
- **Running**: Task is currently executing (logs progress every 30 seconds)
- **Completed**: Task finished successfully, results available
- **Failed**: Task encountered an error, error details available
- **Cancelled**: Task was cancelled by user request

### Asynchronous Task Features
- **Persistent execution**: Tasks continue running even if MCP client disconnects
- **Progress logging**: Automatic progress notifications every 30 seconds
- **Concurrent execution**: Multiple asynchronous tasks can run simultaneously
- **Task cancellation**: Active tasks can be cancelled at any time
- **Result persistence**: Task results remain available until server restart

### Example Workflow
1. Start long-running analysis: `predefined_analysis` with `async: true`
2. Get task ID: `df61ecd4`
3. Monitor progress: `get_task_status` with `task_id: df61ecd4`
4. Check completion: Task status changes to `Completed`
5. View results: Results available in task status response

## IDE Integration

### Claude Code Integration

#### 1. Build and Installation
```powershell
# Build single-file executable
.\Scripts\Publish.ps1
# or
dotnet publish McpProxy\McpProxy.csproj -c Release -r win-x64 -o publish --self-contained true -p:PublishSingleFile=true
```

#### 2. Configuration Options

**Method A: Global Configuration**

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcp": {
    "servers": {
      "windbg-debugging": {
        "command": "D:\\Git\\mcp-windbg\\publish\\McpProxy.exe",
        "args": [],
        "env": {
          "CDB_PATH": "C:\\Program Files\\WindowsApps\\Microsoft.WinDbg_1.2506.12002.0_x64__8wekyb3d8bbwe\\amd64\\cdb.exe",
          "SYMBOL_CACHE": "C:\\Users\\YourUser\\AppData\\Local\\CdbMcpServer\\symbols",
          "SYMBOL_SERVERS": "https://your-company.com/symbols;C:\\MyLocalSymbols",
          "BACKGROUND_SERVICE_URL": "http://localhost:8080"
        }
      }
    }
  }
}
```

**Method B: Project-specific Configuration (Recommended)**

Create `.claude/mcp_config.json` in your project root:

```json
{
  "mcp": {
    "servers": {
      "windbg-debugging": {
        "command": "D:\\Git\\mcp-windbg\\publish\\McpProxy.exe",
        "args": []
      }
    }
  }
}
```

The server will automatically detect debugger installations and use default settings. For custom paths, use environment variables or modify the appsettings.json files in the published directory.

#### 3. Usage in Claude Code

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

#### 4. Advanced Configuration

For production deployment, you can customize configuration by:

1. **Environment Variables in MCP Config:**
```json
"env": {
  "CDB_PATH": "C:\\path\\to\\your\\cdb.exe",
  "SYMBOL_CACHE": "C:\\your\\symbol\\cache",
  "SYMBOL_PATH_EXTRA": "C:\\additional\\symbols",
  "SYMBOL_SERVERS": "https://internal.company.com/symbols;\\\\fileserver\\symbols",
  "BACKGROUND_SERVICE_URL": "http://localhost:8080"
}
```

2. **Modifying appsettings.json after publishing:**
   - Edit `publish/McpProxy/appsettings.json`
   - Edit `publish/BackgroundService/appsettings.json`

3. **Development appsettings:**
   - Create `appsettings.Development.json` files for development-specific settings

### Visual Studio Code Integration

For VS Code users, you can integrate this tool through several approaches:

#### 1. Terminal Integration
```powershell
# Add to your PowerShell profile for quick access
function Analyze-Dump {
    param([string]$DumpPath)
    & "D:\Git\mcp-windbg\Scripts\cdb.ps1" -DumpFile $DumpPath
}

# Usage
Analyze-Dump "C:\crash.dmp"
```

#### 2. Task Configuration
Add to `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Analyze Memory Dump",
      "type": "shell",
      "command": "powershell",
      "args": [
        "-File",
        "${workspaceFolder}/Scripts/cdb.ps1",
        "-DumpFile",
        "${input:dumpFilePath}",
        "-OutputFile",
        "${workspaceFolder}/analysis-report.txt"
      ],
      "group": "build",
      "presentation": {
        "echo": true,
        "reveal": "always",
        "focus": false,
        "panel": "new"
      }
    }
  ],
  "inputs": [
    {
      "id": "dumpFilePath",
      "description": "Path to memory dump file",
      "default": "C:\\dumps\\crash.dmp",
      "type": "promptString"
    }
  ]
}
```

#### 3. Extension Integration
You can create a simple VS Code extension to integrate the MCP server:

1. Install the MCP extension development tools
2. Configure the MCP server endpoint in VS Code settings
3. Use the Command Palette to execute debugging commands

### GitHub Copilot Integration

GitHub Copilot can assist with memory dump analysis by providing context-aware suggestions:

#### 1. Code Generation for Analysis Scripts
Ask Copilot to generate WinDbg commands based on crash symptoms:

```csharp
// Example: Generate heap analysis commands
// Copilot can suggest: !heap -s, !heap -stat, !heap -flt s <size>
```

#### 2. Automated Script Templates
Use Copilot to generate PowerShell scripts for batch analysis:

```powershell
# Copilot can help create scripts like:
# - Batch processing multiple dump files
# - Custom report generation
# - Symbol path configuration
```

#### 3. Integration with Chat Features
In VS Code with Copilot Chat, you can ask:
- "Generate a WinDbg command to analyze heap corruption"
- "Create a PowerShell script to batch process crash dumps"
- "Explain this stack trace from a memory dump"

#### 4. Custom Copilot Prompts for Debugging
Create custom prompts in your workspace:

```json
// .vscode/settings.json
{
  "github.copilot.chat.welcomeMessage": "I can help you analyze memory dumps using WinDbg/CDB commands. Ask me about:\n- Heap analysis\n- Stack trace interpretation\n- Exception debugging\n- Performance analysis",
  "github.copilot.enable": {
    "*": true,
    "markdown": true,
    "powershell": true
  }
}
```

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