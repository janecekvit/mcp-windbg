# Build script for single-file publishing
param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory=$false)]
    [string]$Runtime = "win-x64",

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "..\publish"
)

Write-Host "🚀 Publishing Dump Analysis Service Projects..." -ForegroundColor Green
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

# Publish Dump Analysis Service (MCP HTTP Server + REST API)
Write-Host "📦 Publishing DumpAnalysisService (MCP HTTP + REST API)..." -ForegroundColor Cyan
dotnet publish ..\DumpAnalysisService\DumpAnalysisService.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ DumpAnalysisService build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

# Publish Command Line Client (Command-line tool)
Write-Host "📦 Publishing CommandLineClient (CLI tool)..." -ForegroundColor Cyan
dotnet publish ..\CommandLineClient\CommandLineClient.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ CommandLineClient build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

if ($buildSuccess) {
    Write-Host "✅ All builds completed successfully!" -ForegroundColor Green
    Write-Host

    # Check DumpAnalysisService executable
    $bgExePath = Join-Path $OutputDir "DumpAnalysisService.exe"
    if (Test-Path $bgExePath) {
        $fileInfo = Get-Item $bgExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 DumpAnalysisService: $bgExePath" -ForegroundColor Green
        Write-Host "   Size: $sizeMB MB" -ForegroundColor Gray
    }

    # Check CommandLineClient executable
    $clientExePath = Join-Path $OutputDir "CommandLineClient.exe"
    if (Test-Path $clientExePath) {
        $fileInfo = Get-Item $clientExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 CommandLineClient: $clientExePath" -ForegroundColor Green
        Write-Host "   Size: $sizeMB MB" -ForegroundColor Gray
    }

    Write-Host "`n🎯 Usage:" -ForegroundColor Yellow
    Write-Host "  1. Start DumpAnalysisService:" -ForegroundColor White
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
    Write-Host "📖 For more information, see README.md" -ForegroundColor Gray
} else {
    Write-Host "❌ Some builds failed!" -ForegroundColor Red
    exit 1
}
