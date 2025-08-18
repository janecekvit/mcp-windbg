# CDB MCP Server Integration with Claude Code

## 1. Prepare Executable

Build the server as a single-file executable:
```powershell
# Run from Scripts directory
.\Publish.ps1
```

## 2. Claude Code Configuration

### Method A: Global Configuration

Add to your global Claude Code config file:

**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcp": {
    "servers": {
      "cdb-debugging": {
        "command": "D:\\Git\\mcp-windbg\\publish\\McpProxy.exe",
        "args": [],
        "env": {
          "CDB_PATH": "C:\\Program Files\\WindowsApps\\Microsoft.WinDbg_1.2506.12002.0_x64__8wekyb3d8bbwe\\amd64\\cdb.exe",
          "SYMBOL_CACHE": "C:\\Users\\DeWitt\\AppData\\Local\\CdbMcpServer\\symbols"
        }
      }
    }
  }
}
```

### Method B: Project-specific Configuration

Create `.claude/mcp_config.json` in your project root:

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

## 3. Available MCP Tools

After configuration, you'll have access to these tools in Claude Code:

### `detect_debuggers`
- **Description**: Detects available CDB/WinDbg installations
- **Usage**: Diagnostics and configuration verification

### `load_dump` 
- **Description**: Loads memory dump and creates debugging session
- **Parameters**: `dump_file_path` - path to .dmp file
- **Output**: Returns session ID for further operations

### `execute_command`
- **Description**: Executes custom CDB/WinDbg command
- **Parameters**: `session_id`, `command`
- **Examples**: `"kb"`, `"!analyze -v"`, `"dt ntdll!_PEB"`

### `basic_analysis`
- **Description**: Complete basic analysis (equivalent to PowerShell script)
- **Parameters**: `session_id`
- **Output**: Detailed crash analysis

### `predefined_analysis`
- **Description**: Specialized analysis by type
- **Parameters**: `session_id`, `analysis_type`
- **Types**: `basic`, `exception`, `threads`, `heap`, `modules`, `handles`, `locks`, `memory`, `drivers`, `processes`

### `list_sessions`
- **Description**: Shows all active debugging sessions

### `list_analyses`
- **Description**: Shows available analysis types with descriptions

### `close_session`
- **Description**: Closes debugging session
- **Parameters**: `session_id`

## 4. Usage Examples in Claude Code

### Basic Workflow:
1. `detect_debuggers` - verify configuration
2. `load_dump` with path to dump file → get session_id  
3. `basic_analysis` for quick overview
4. `execute_command` for specialized commands
5. `close_session` when finished

### Advanced Usage:
- `predefined_analysis` with `analysis_type: "heap"` for heap analysis
- `execute_command` with `"!clrstack"` for .NET stack traces
- `execute_command` with `"lm"` for module listing

## 5. Environment Variables (Optional)

If auto-detection doesn't work, configure:

```json
"env": {
  "CDB_PATH": "C:\\path\\to\\your\\cdb.exe",
  "SYMBOL_CACHE": "C:\\your\\symbol\\cache",
  "SYMBOL_PATH_EXTRA": "C:\\additional\\symbols"
}
```

## 6. Restart Claude Code

After adding configuration, restart Claude Code to load the MCP server.

## 7. Verify Functionality

In Claude Code, enter:
> "Use detect_debuggers tool to verify configuration"

You should see information about found debuggers and configuration.