# Build script for single-file publishing
param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory=$false)]
    [string]$Runtime = "win-x64",

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "..\publish"
)

Write-Host "🚀 Publishing WinDbg MCP Server Projects..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Runtime: $Runtime" -ForegroundColor Yellow
Write-Host "Output: $OutputDir" -ForegroundColor Yellow
Write-Host

# Clean previous builds
if (Test-Path $OutputDir) {
    Write-Host "🧹 Cleaning previous build..." -ForegroundColor Cyan
    Remove-Item $OutputDir -Recurse -Force
}

$buildSuccess = $true

# Publish Background Service (MCP HTTP Server + REST API)
Write-Host "📦 Publishing BackgroundService (MCP HTTP + REST API)..." -ForegroundColor Cyan
dotnet publish ..\BackgroundService\BackgroundService.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ BackgroundService build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

# Publish CdbDebuggerClient (Command-line tool)
Write-Host "📦 Publishing CdbDebuggerClient (CLI tool)..." -ForegroundColor Cyan
dotnet publish ..\CdbDebuggerClient\CdbDebuggerClient.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ CdbDebuggerClient build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

if ($buildSuccess) {
    Write-Host "✅ All builds completed successfully!" -ForegroundColor Green
    Write-Host

    # Check BackgroundService executable
    $bgExePath = Join-Path $OutputDir "BackgroundService.exe"
    if (Test-Path $bgExePath) {
        $fileInfo = Get-Item $bgExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 BackgroundService: $bgExePath" -ForegroundColor Green
        Write-Host "   Size: $sizeMB MB" -ForegroundColor Gray
    }

    # Check CdbDebuggerClient executable
    $clientExePath = Join-Path $OutputDir "CdbDebuggerClient.exe"
    if (Test-Path $clientExePath) {
        $fileInfo = Get-Item $clientExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 CdbDebuggerClient: $clientExePath" -ForegroundColor Green
        Write-Host "   Size: $sizeMB MB" -ForegroundColor Gray
    }

    Write-Host "`n🎯 Usage:" -ForegroundColor Yellow
    Write-Host "  1. Start BackgroundService:" -ForegroundColor White
    Write-Host "     $bgExePath" -ForegroundColor Cyan
    Write-Host
    Write-Host "  2. Configure Claude Code (.mcp.json):" -ForegroundColor White
    Write-Host '     {' -ForegroundColor Cyan
    Write-Host '       "mcpServers": {' -ForegroundColor Cyan
    Write-Host '         "dump-analyzer": {' -ForegroundColor Cyan
    Write-Host '           "type": "http",' -ForegroundColor Cyan
    Write-Host '           "url": "http://localhost:7997/mcp"' -ForegroundColor Cyan
    Write-Host '         }' -ForegroundColor Cyan
    Write-Host '       }' -ForegroundColor Cyan
    Write-Host '     }' -ForegroundColor Cyan
    Write-Host
    Write-Host "  3. Use CLI tool (optional - for scripting/Azure Functions):" -ForegroundColor White
    Write-Host "     $clientExePath load C:\dumps\crash.dmp" -ForegroundColor Cyan
    Write-Host "     $clientExePath exec <session-id> `"!analyze -v`"" -ForegroundColor Cyan
    Write-Host
    Write-Host "🔧 Symbol Configuration (3 methods, highest to lowest priority):" -ForegroundColor Yellow
    Write-Host "  1. Tool parameters: Load dump D:\crash.dmp with symbol_cache=`"D:\Symbols`"" -ForegroundColor White
    Write-Host "  2. HTTP headers in .mcp.json: Per-MCP-client configuration" -ForegroundColor White
    Write-Host "  3. appsettings.json: Server-wide defaults for all clients" -ForegroundColor White
    Write-Host "  (See README.md for detailed examples)" -ForegroundColor Gray
    Write-Host
    Write-Host "🔧 Optional Environment Variable:" -ForegroundColor Yellow
    Write-Host "  CDB_PATH - Custom path to cdb.exe or windbg.exe (overrides auto-detection)" -ForegroundColor White
    Write-Host
    Write-Host "📖 For more information, see README.md" -ForegroundColor Gray
} else {
    Write-Host "❌ Some builds failed!" -ForegroundColor Red
    exit 1
}
