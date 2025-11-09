# WinDbg MCP Server

MCP (Model Context Protocol) server for interactive debugging of Windows memory dump files using Microsoft Command Line Debugger (cdb.exe) or WinDbg.

## Features

- **Interactive debugging**: Persistent CDB sessions for each dump file
- **Predefined analyses**: 10 types of specialized analyses (basic, exception, threads, heap, etc.)
- **Custom commands**: Ability to execute custom WinDbg/CDB commands
- **Session management**: Concurrent work with multiple dump files
- **MCP HTTP protocol**: Standard MCP over HTTP for seamless integration
- **Real-time progress**: SignalR-based progress notifications
- **Command-line client**: Standalone client for scripting and Azure Functions

## Architecture

```
┌─────────────┐
│ Claude Code │  MCP HTTP
└──────┬──────┘
       │ http://localhost:7997/mcp
       ↓
┌──────────────────────────────────────────────────┐
│          BackgroundService (port 7997)           │
│                                                  │
│  ┌──────────────┐      ┌───────────────────┐   │
│  │ MCP HTTP     │      │ REST API          │   │
│  │ /mcp/*       │      │ /api/jobs/*       │   │
│  └──────────────┘      └───────────────────┘   │
│                                                  │
│         ┌──────────────────────┐                │
│         │ SessionManager       │                │
│         │ + JobManager         │                │
│         │ + ProgressHub        │                │
│         └──────────┬───────────┘                │
└────────────────────┼────────────────────────────┘
                     ↓
              ┌─────────────┐
              │ CDB Process │
              └─────────────┘

┌────────────────┐
│ PowerShell /   │  REST API
│ Azure Function │
└────────┬───────┘
         │ http://localhost:7997/api
         ↓
    (BackgroundService)

┌──────────────────┐
│ CdbDebuggerClient│  REST API + SignalR
└────────┬─────────┘
         │ http://localhost:7997
         ↓
    (BackgroundService)
```

## Requirements

- .NET 9.0+
- Windows SDK Debuggers (cdb.exe)
- Access to Microsoft symbol server (for downloading symbols)

## Installation

### Quick Installation (Single-file executable)
```powershell
# Build single-file executables
.\Scripts\Publish.ps1

# Start the MCP server
.\publish\BackgroundService.exe

# Or use the command-line client
.\publish\CdbDebuggerClient.exe help
```

### Development build
```bash
dotnet build
dotnet run --project BackgroundService
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
2. **Default values**

### Configuration File

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

### list_jobs
Lists all jobs with their current status.

**Parameters (optional):**
- `state`: Filter by job state (Queued, Running, Completed, Failed, Cancelled)

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

## IDE Integration

### Claude Code Integration

#### 1. Build and Installation
```powershell
# Build single-file executable
.\Scripts\Publish.ps1
# or
dotnet publish BackgroundService\BackgroundService.csproj -c Release -r win-x64 -o publish --self-contained true -p:PublishSingleFile=true
```

#### 2. Configuration Options

**Method A: Global Configuration**

Add to `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "dump-analyzer": {
      "type": "http",
      "url": "http://localhost:7997/mcp",
      "headers": {
        "X-Symbol-Cache": "D:\\Symbols",
        "X-Symbol-Path-Extra": "",
        "X-Symbol-Servers": ""
      }
    }
  }
}
```

**Important:** You must start BackgroundService.exe manually before using Claude Code:
```powershell
# Start the service (keep this running)
.\publish\BackgroundService.exe
**Method B: Project-specific Configuration (Recommended)**

Create `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "dump-analyzer": {
      "type": "http",
      "url": "http://localhost:7997/mcp",
      "headers": {
        "X-Symbol-Cache": "D:\\Symbols",
        "X-Symbol-Path-Extra": "",
        "X-Symbol-Servers": ""
      }
    }
  }
}
```

Start BackgroundService separately:
```powershell
# In a separate terminal/PowerShell window
cd D:\Git\mcp-windbg\publish
.\BackgroundService.exe
```

**Symbol Configuration Options:**

The `load_dump` tool supports three configuration methods with priority order:

**1. Tool Parameters (Highest Priority - Per-Call)**

Pass symbol configuration directly in your prompt:
```
Load dump file D:\crash.dmp with symbol_cache="D:\Symbols" and symbol_servers="https://symbols.company.com"
```

Optional parameters:
- `symbol_cache` - Symbol cache directory
- `symbol_path_extra` - Additional local symbol directories (semicolon-separated)
- `symbol_servers` - Custom symbol servers (semicolon-separated)

**2. HTTP Headers (Per-MCP-Client Configuration)**

Configure in your `.mcp.json` to set defaults for your MCP client:
```json
{
  "mcpServers": {
    "dump-analyzer": {
      "type": "http",
      "url": "http://localhost:7997/mcp",
      "headers": {
        "X-Symbol-Cache": "D:\\Symbols",
        "X-Symbol-Path-Extra": "C:\\MyProject\\Debug",
        "X-Symbol-Servers": "https://symbols.company.com"
      }
    }
  }
}
```

**3. appsettings.json (Server-Wide Defaults)**

Edit `publish/appsettings.json` or `BackgroundService/appsettings.json` to set defaults for all MCP clients:
```json
{
  "Debugger": {
    "DefaultSymbolCache": "D:\\Symbols",
    "DefaultSymbolPathExtra": "C:\\MyProject\\Debug",
    "DefaultSymbolServers": "https://symbols.company.com"
  }
}
```

#### 3. Usage in Claude Code

After starting BackgroundService and configuring Claude Code, you can use:

- `detect_debuggers` - verify debugger configuration
- `load_dump` - load dump file and create session
- `basic_analysis` - complete crash analysis
- `execute_command` - custom CDB commands
- `predefined_analysis` - specialized analyses (heap, threads, modules, etc.)

**Example workflow:**
1. Start BackgroundService.exe
2. Open Claude Code
3. "Use detect_debuggers to verify configuration"
4. "Load dump file D:\\crash.dmp with symbol_cache='D:\\Symbols'"
5. "Perform basic_analysis on session"
6. "Run predefined_analysis of type heap"

#### 4. Advanced Configuration

**Configuration Priority (from highest to lowest):**
1. **Tool Parameters** - Per-call override via prompt
2. **HTTP Headers** - Per-MCP-client configuration via `.mcp.json`
3. **appsettings.json** - Server-wide defaults for all clients

**Use Cases:**

- **Tool Parameters**: Override symbol configuration for a specific dump file
  ```
  Load dump D:\crash.dmp with symbol_servers="https://internal.company.com/symbols;\\fileserver\symbols"
  ```

- **HTTP Headers**: Configure defaults for your MCP client instance (e.g., your Claude Code)
  ```json
  {
    "mcpServers": {
      "dump-analyzer": {
        "type": "http",
        "url": "http://localhost:7997/mcp",
        "headers": {
          "X-Symbol-Cache": "D:\\MySymbols",
          "X-Symbol-Path-Extra": "C:\\MyProject\\Debug;D:\\ThirdParty\\Symbols",
          "X-Symbol-Servers": "https://internal.company.com/symbols"
        }
      }
    }
  }
  ```

- **appsettings.json**: Set server-wide defaults for all MCP clients
  ```json
  {
    "Debugger": {
      "DefaultSymbolCache": "D:\\SymbolCache",
      "DefaultSymbolPathExtra": "C:\\CommonSymbols",
      "DefaultSymbolServers": "https://company-symbols.com"
    }
  }
  ```

**Example Scenario:**
- Server (`appsettings.json`): Default cache `D:\ServerSymbols`
- Your MCP client (`.mcp.json` headers): Override to `D:\MySymbols`
- Specific call (tool parameter): Override to `D:\ProjectSymbols` for one dump

Create `appsettings.Development.json` for development-specific settings.

## Command-Line Client (CdbDebuggerClient)

Standalone command-line client for scripting, Azure Functions, and automation.

### Installation
```powershell
# Build with Publish.ps1
.\Scripts\Publish.ps1

# Client is in publish\CdbDebuggerClient.exe
```

### Usage

**Start BackgroundService first:**
```powershell
# Terminal 1: Start the service
.\publish\BackgroundService.exe
```

**Then use the client:**
```powershell
# Terminal 2: Use the client
cd .\publish

# Load dump
.\CdbDebuggerClient.exe load "C:\dumps\crash.dmp"

# Execute command
.\CdbDebuggerClient.exe exec session-id "!analyze -v"

# Run analysis
.\CdbDebuggerClient.exe analyze session-id

# List jobs
.\CdbDebuggerClient.exe list-jobs

# Close session
.\CdbDebuggerClient.exe close session-id
```

### Symbol Configuration
```powershell
# Via command line parameters
.\CdbDebuggerClient.exe --symbol-cache "D:\Symbols" load "C:\dumps\crash.dmp"
```

### Azure Functions Integration

```csharp
// Example Azure Function using CdbDebuggerClient libraries
using Shared.Client;
using Microsoft.Extensions.Logging;

public static async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
    ILogger log)
{
    var httpClient = new HttpClient();
    var signalRClient = new SignalRClientService(log, "http://localhost:7997/hubs/progress");
    await signalRClient.ConnectAsync();

    var apiService = new DebuggerApiService(log, httpClient, signalRClient, "http://localhost:7997");

    var dumpPath = req.Query["dumpPath"];
    var sessionId = await apiService.LoadDumpAsync(dumpPath);
    var analysis = await apiService.BasicAnalysisAsync(sessionId);

    return new OkObjectResult(analysis);
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

## PowerShell Script Usage

For standalone usage without MCP:

```powershell
# Analyze dump using PowerShell script
.\Scripts\cdb.ps1 -DumpFile "C:\dumps\crash.dmp" -OutputFile "analysis.txt"
```

## Troubleshooting

### BackgroundService won't start
- Check port 7997 is not in use: `netstat -ano | findstr :7997`
- Verify CDB is installed: run `detect_debuggers` tool
- Check logs in console output

### Symbol loading is slow
- First load downloads symbols (10-30 min)
- Subsequent loads use cache (30-60 sec)
- Configure symbol cache in appsettings.json for persistent cache

### Claude Code can't connect
- Ensure BackgroundService.exe is running
- Verify configuration in `.mcp.json` or Claude config
- Check http://localhost:7997/api/health in browser

## License

MIT License - see LICENSE file for details
