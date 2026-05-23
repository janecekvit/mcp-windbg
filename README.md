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
│        DumpAnalysisService (port 7997)           │
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
    (DumpAnalysisService)

┌──────────────────┐
│ CommandLineClient│  REST API + SignalR
└────────┬─────────┘
         │ http://localhost:7997
         ↓
    (DumpAnalysisService)
```

## Requirements

- .NET 10
- Windows SDK Debuggers (cdb.exe)
- Access to Microsoft symbol server (for downloading symbols)

## Installation

### Quick Installation (Single-file executable)
```powershell
# Build single-file executables
.\Scripts\Publish.ps1

# Start the MCP server
.\publish\win-x64\DumpAnalysisService.exe

# Or use the command-line client
.\publish\win-x64\CommandLineClient.exe help
```

### Development build
```bash
dotnet build
dotnet run --project DumpAnalysisService
```

## Running via Docker

Pre-built Windows container images are published to GitHub Container Registry on every release tag. Lets you run `DumpAnalysisService` as a sidecar without manually downloading the Release ZIP.

### Prerequisites

- **Windows host** with one of:
  - Docker Desktop in **Windows-containers** mode (Windows 10/11 Pro/Enterprise), or
  - Containers role on Windows Server 2019/2022.
- **Linux hosts cannot run Windows containers.** The image is Windows Server Core ltsc2022 — no Linux variant exists because `cdb.exe` is Windows-native.

### Quick start

```powershell
docker run -d --name windbg `
  -p 7997:7997 `
  -v C:\dumps:C:\dumps:ro `
  -v cdb-symbols:C:\symbols `
  ghcr.io/janecekvit/mcp-windbg:latest
```

No `-e Debugger__DefaultSymbolCache` is needed — the image already defaults the cache to `C:\symbols`, so you only mount a volume there. To override the location, see [Environment variables](#environment-variables).

Then call the REST API:

```powershell
Invoke-RestMethod http://localhost:7997/api/jobs   # empty array []
```

### Connecting an MCP client / AI from outside the container

The service binds to all interfaces (`ASPNETCORE_URLS=http://+:7997`, baked into
the image), so the published port is reachable from the host or another machine.
Point your MCP client at:

- **Local host:** `http://localhost:7997/mcp`
- **Remote / Azure:** `http://<public-host-or-ip>:7997/mcp`

REST API and SignalR live under the same host at `/api` and `/hubs/progress`.

### docker-compose example

```yaml
services:
  windbg:
    image: ghcr.io/janecekvit/mcp-windbg:vX.Y.Z   # pin a real release tag, not :latest
    ports:
      - "7997:7997"
    volumes:
      - C:\dumps:C:\dumps:ro
      - cdb-symbols:C:\symbols
    environment:
      Debugger__DefaultSymbolCache: C:\symbols
      Debugger__DefaultSymbolPathExtra: ""
      Debugger__DefaultSymbolServers: ""
    restart: unless-stopped

volumes:
  cdb-symbols:
```

### Environment variables

ASP.NET Core maps `Section__Key` env vars onto the `appsettings.json` tree, so the three debugger settings are:

| Env var | Maps to | Purpose |
|---|---|---|
| `Debugger__DefaultSymbolCache` | `Debugger:DefaultSymbolCache` | Local symbol cache directory inside the container (default in the container image: `C:\symbols` — mount a volume there so symbols persist across restarts; outside Docker the .exe defaults to `%LOCALAPPDATA%\DumpAnalysisService\symbols`) |
| `Debugger__DefaultSymbolPathExtra` | `Debugger:DefaultSymbolPathExtra` | Semicolon-separated extra local symbol paths (e.g. `C:\my-pdbs;\\server\symbols`) |
| `Debugger__DefaultSymbolServers` | `Debugger:DefaultSymbolServers` | Semicolon-separated extra symbol servers; Microsoft public symbol server is always included |

### Pin a version

Always pin to `:vX.Y.Z` in production / scripted use. `:latest` is convenient locally but will silently roll forward when a new tag is pushed.

### Known limitations

- **First pull is ~3 GB** (Windows Server Core base + Debugging Tools + service binaries). Subsequent pulls only fetch changed layers.
- **First `load_dump` against a new symbol cache takes 10-30 minutes** while Microsoft Symbol Server downloads PDBs. Second run from the cached volume: 30-60 seconds.
- **Dump file paths must be visible inside the container** — mount the host directory containing dumps (e.g. `-v C:\dumps:C:\dumps:ro`) and pass that *container* path in `DumpPath`, not the host path.
- Symbol cache is intentionally **not pre-baked** into the image — symbols rotate every Patch Tuesday, so a baked cache would just rot. Mount a named volume (`cdb-symbols`) to make the cache survive container restarts.

### Deploying to Azure

The same image runs in Azure with configuration supplied entirely through app
settings (which Azure exposes as environment variables) — no rebuild, no
ENTRYPOINT edits. Pick a **Windows-container-capable** service:

| Service | Fit |
|---|---|
| Azure Container Instances (ACI) | Cheapest; good for a single instance or one instance per customer; no load balancer or autoscaling. |
| App Service for Containers (Windows) | Simplest managed option; built-in scale-out + load balancer (requires a Windows-container-capable plan, e.g. Premium v3). |
| AKS (Windows node pool) | Full orchestrator: autoscaling, load balancing, health probes across replicas. |

> **Azure Container Apps is NOT an option** — it does not support Windows
> containers, and `cdb.exe` is Windows-native (no Linux build exists).

**Shared symbol cache across replicas:** for the autoscaled options (App Service,
AKS), mount an **Azure Files** SMB share on `C:\symbols` in *every* replica.
Symbols download once and are shared behind the load balancer instead of each
replica re-downloading them. Azure Files supports concurrent multi-mount access;
the symsrv cache is read-mostly and uses file locking, so concurrent-write risk
is low. Set `Debugger__DefaultSymbolCache=C:\symbols` (already the image default)
and point the mount at it.

Deployment manifests themselves live in a separate repository — this section
documents only the image contract those manifests rely on.

## Automatic Debugger Detection

Server automatically detects available CDB/WinDbg installations:

- **Windows SDK** (preferred): `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe`
- **WinDbg Store App**: `C:\Program Files\WindowsApps\Microsoft.WinDbg_*\amd64\windbg.exe`
- **Various architectures**: x64, x86, amd64

Use the `detect_debuggers` tool to discover available installations.

## Configuration

The server supports multiple configuration methods with the following priority order (highest to lowest):
1. **Tool Parameters** — per-call overrides passed as MCP tool arguments (e.g. `load_dump` `symbol_cache`)
2. **HTTP Headers** — per-MCP-client defaults via `.mcp.json` (`X-Symbol-Cache`, `X-Symbol-Path-Extra`, `X-Symbol-Servers`)
3. **appsettings.json** — server-wide defaults (`Debugger:DefaultSymbolCache`, …)
4. **Built-in defaults**

### Configuration File

#### DumpAnalysisService Configuration
Create `DumpAnalysisService/appsettings.json`:
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
- `symbol_cache` *(optional)*: Symbol cache directory (overrides HTTP header and `appsettings.json` defaults)
- `symbol_path_extra` *(optional)*: Semicolon-separated additional local symbol paths
- `symbol_servers` *(optional)*: Semicolon-separated extra symbol servers (Microsoft public symbol server is always included)

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

## API Reference

In addition to MCP, `DumpAnalysisService` exposes the same job-based operations over a plain REST API (for PowerShell, Azure Functions, or any HTTP client) and the standard MCP `tasks/*` protocol methods.

### REST endpoints

All job-creating endpoints return `202 Accepted` with `{ jobId, statusEndpoint }`; poll `GET /api/jobs/{jobId}` until `state` is `Completed`, `Failed`, or `Cancelled`. Authoritative routes live in `JobsController.cs` and `DiagnosticsController.cs`.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/jobs/load-dump` | Create session by loading a dump file |
| `POST` | `/api/jobs/execute-command` | Run a WinDbg/CDB command in an existing session |
| `POST` | `/api/jobs/basic-analysis` | Run the comprehensive basic analysis |
| `POST` | `/api/jobs/predefined-analysis` | Run a named specialised analysis (heap, threads, …) |
| `POST` | `/api/jobs/close-session` | Close a session and release CDB |
| `GET`  | `/api/jobs` | List jobs (optional `?state=Running`) |
| `GET`  | `/api/jobs/{jobId}` | Job status + progress |
| `POST` | `/api/jobs/{jobId}/cancel` | Cancel a running job |
| `GET`  | `/api/diagnostics/health` | Liveness probe (used by Docker `HEALTHCHECK`) |
| `GET`  | `/api/diagnostics/detect-debuggers` | Enumerate installed CDB/WinDbg installations |
| `GET`  | `/api/diagnostics/analyses` | List available predefined analysis types |

### MCP `tasks/*` adapter

The service implements `IMcpTaskStore` (`DumpAnalysisService/Tasks/JobManagerBackedTaskStore.cs`), so MCP clients can use the standard `tasks/list`, `tasks/get`, `tasks/result`, and `tasks/cancel` protocol methods instead of polling REST. One MCP task ID maps 1:1 to one REST job ID — both surfaces read from the same underlying `JobManagerService`, so streaming clients (`IProgress<ProgressNotificationValue>` notifications) and polling clients see identical state.

> `IMcpTaskStore` is experimental in MCP SDK 1.3.0 (diagnostic `MCPEXP001`). The experimental surface is isolated to the adapter file.

### SignalR progress hub

For real-time progress (instead of polling), connect to the SignalR hub at `/hubs/progress` and subscribe to a `jobId`. `CommandLineClient` uses this path via `Shared.Client.SignalRClientService`; the same approach works from any SignalR-capable client. Minimal C# example:

```csharp
using Shared.Client;
var signalR = new SignalRClientService(logger, "http://localhost:7997/hubs/progress");
await signalR.ConnectAsync();
var api = new DebuggerApiService(logger, new HttpClient(), signalR, "http://localhost:7997");
var sessionId = await api.LoadDumpAsync(@"C:\dumps\crash.dmp"); // polls REST; SignalR delivers progress callbacks
```

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
dotnet publish DumpAnalysisService\DumpAnalysisService.csproj -c Release -r win-x64 -o publish\win-x64 --self-contained true -p:PublishSingleFile=true
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

**Important:** You must start DumpAnalysisService.exe manually before using Claude Code:
```powershell
# Start the service (keep this running)
.\publish\win-x64\DumpAnalysisService.exe
```

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

Start DumpAnalysisService separately:
```powershell
# In a separate terminal/PowerShell window
cd D:\Git\mcp-windbg\publish\win-x64
.\DumpAnalysisService.exe
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

Edit `publish\win-x64\appsettings.json` or `DumpAnalysisService/appsettings.json` to set defaults for all MCP clients:
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

After starting DumpAnalysisService and configuring Claude Code, you can use:

- `detect_debuggers` - verify debugger configuration
- `load_dump` - load dump file and create session
- `basic_analysis` - complete crash analysis
- `execute_command` - custom CDB commands
- `predefined_analysis` - specialized analyses (heap, threads, modules, etc.)

**Example workflow:**
1. Start DumpAnalysisService.exe
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

## Command-Line Client (CommandLineClient)

Standalone command-line client for scripting, Azure Functions, and automation.

### Installation
```powershell
# Build with Publish.ps1
.\Scripts\Publish.ps1

# Client is in publish\win-x64\CommandLineClient.exe
```

### Usage

**Start DumpAnalysisService first:**
```powershell
# Terminal 1: Start the service
.\publish\win-x64\DumpAnalysisService.exe
```

**Then use the client:**
```powershell
# Terminal 2: Use the client
cd .\publish\win-x64

# Load dump
.\CommandLineClient.exe load "C:\dumps\crash.dmp"

# Execute command
.\CommandLineClient.exe exec session-id "!analyze -v"

# Run analysis
.\CommandLineClient.exe analyze session-id

# List jobs
.\CommandLineClient.exe list-jobs

# Close session
.\CommandLineClient.exe close session-id
```

### Symbol Configuration
```powershell
# Via command line parameters
.\CommandLineClient.exe --symbol-cache "D:\Symbols" load "C:\dumps\crash.dmp"
```

### Azure Functions Integration

```csharp
// Example Azure Function using CommandLineClient libraries
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

## Manual REST API testing

Quick PowerShell snippets for poking the REST API without an MCP client — useful for smoke tests, scripting, and debugging. For end-to-end testing against a real, reproducible dump, build and run `DumpAnalysisService.TestCrasher`, which self-mini-dumps via `dbghelp!MiniDumpWriteDump` (no third-party crashing app required):

```powershell
# Generate a fresh, hermetic dump (TestCrasher dumps itself, then exits)
dotnet run --project DumpAnalysisService.TestCrasher -- C:\dumps\crash.dmp
```

Service must be running at `http://localhost:7997`. All operations are job-based: `POST` returns `202 Accepted` with a `jobId`; poll `GET /api/jobs/{jobId}` until `state` is `Completed`, `Failed`, or `Cancelled`.

```powershell
# 1. Load the dump → returns { jobId, statusEndpoint, message }
$create = Invoke-RestMethod -Method Post `
  -Uri http://localhost:7997/api/jobs/load-dump `
  -ContentType application/json `
  -Body (@{ dumpFilePath = 'C:\dumps\crash.dmp' } | ConvertTo-Json)

# 2. Poll until the load-dump job completes; the resulting sessionId is what every later call needs
do {
    Start-Sleep -Seconds 1
    $job = Invoke-RestMethod http://localhost:7997/api/jobs/$($create.jobId)
} until ($job.state -in 'Completed','Failed','Cancelled')
$sessionId = $job.sessionId
```

Once you have a `sessionId`, the other three operations follow the same pattern (POST → poll the returned `jobId` → read `result` from the final job status):

```powershell
# Run !analyze -v + thread stacks + module list
Invoke-RestMethod -Method Post -Uri http://localhost:7997/api/jobs/basic-analysis `
  -ContentType application/json `
  -Body (@{ sessionId = $sessionId } | ConvertTo-Json)

# Execute an arbitrary CDB command
Invoke-RestMethod -Method Post -Uri http://localhost:7997/api/jobs/execute-command `
  -ContentType application/json `
  -Body (@{ sessionId = $sessionId; command = '!heap -s' } | ConvertTo-Json)

# Run a named predefined analysis (basic | exception | threads | heap | modules | handles | locks | memory | drivers | processes)
Invoke-RestMethod -Method Post -Uri http://localhost:7997/api/jobs/predefined-analysis `
  -ContentType application/json `
  -Body (@{ sessionId = $sessionId; analysisType = 'heap' } | ConvertTo-Json)
```

`DumpAnalysisService.IntegrationTests` automates this exact flow against a `TestCrasher`-generated dump and is the recommended pattern for verifying any change end-to-end.

## PowerShell Script Usage

For standalone usage without MCP:

```powershell
# Analyze dump using PowerShell script
.\Scripts\cdb.ps1 -DumpFile "C:\dumps\crash.dmp" -OutputFile "analysis.txt"
```

## Troubleshooting

### DumpAnalysisService won't start
- Check port 7997 is not in use: `netstat -ano | findstr :7997`
- Verify CDB is installed: run `detect_debuggers` tool
- Check logs in console output

### Symbol loading is slow
- First load downloads symbols (10-30 min)
- Subsequent loads use cache (30-60 sec)
- Configure symbol cache in appsettings.json for persistent cache

### Claude Code can't connect
- Ensure DumpAnalysisService.exe is running
- Verify configuration in `.mcp.json` or Claude config
- Check http://localhost:7997/api/diagnostics/health in browser

## License

MIT License - see LICENSE file for details
