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
    "SymbolPathExtra": ""
  }
}
```

### Environment Variables (Optional Fallback)

- `CDB_PATH`: Custom path to cdb.exe/windbg.exe (overrides auto-detection)
- `SYMBOL_CACHE`: Local cache for symbols (default: `%LOCALAPPDATA%\CdbMcpServer\symbols`)
- `SYMBOL_PATH_EXTRA`: Additional symbol paths
- `BACKGROUND_SERVICE_URL`: Background service endpoint (default: `http://localhost:8080`)

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