# Build script pro single-file publishing
param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false)]
    [string]$Runtime = "win-x64",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "..\publish"
)

Write-Host "🚀 Publishing CDB MCP Projects..." -ForegroundColor Green
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

# Publish MCP Proxy
Write-Host "📦 Publishing MCP Proxy..." -ForegroundColor Cyan
dotnet publish ..\McpProxy\McpProxy.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ MCP Proxy build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

# Publish Background Service
Write-Host "📦 Publishing Background Service..." -ForegroundColor Cyan
dotnet publish ..\BackgroundService\BackgroundService.csproj -c $Configuration -r $Runtime -o $OutputDir --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Background Service build failed!" -ForegroundColor Red
    $buildSuccess = $false
}

if ($buildSuccess) {
    Write-Host "✅ All builds completed successfully!" -ForegroundColor Green
    
    # Check MCP Proxy executable
    $mcpExePath = Join-Path $OutputDir "McpProxy.exe"
    if (Test-Path $mcpExePath) {
        $fileInfo = Get-Item $mcpExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 MCP Proxy: $mcpExePath" -ForegroundColor Green
        Write-Host "📏 Size: $sizeMB MB" -ForegroundColor Green
    }
    
    # Check Background Service executable
    $bgExePath = Join-Path $OutputDir "BackgroundService.exe"
    if (Test-Path $bgExePath) {
        $fileInfo = Get-Item $bgExePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "📁 Background Service: $bgExePath" -ForegroundColor Green
        Write-Host "📏 Size: $sizeMB MB" -ForegroundColor Green
    }
    
    Write-Host "`n🎯 Usage:" -ForegroundColor Yellow
    Write-Host "  MCP Proxy: $mcpExePath" -ForegroundColor White
    Write-Host "  Background Service: $bgExePath" -ForegroundColor White
    Write-Host "`n🔧 Environment Variables (optional):" -ForegroundColor Yellow
    Write-Host "  CDB_PATH - Custom path to cdb.exe or windbg.exe" -ForegroundColor White
    Write-Host "  SYMBOL_CACHE - Custom symbol cache directory" -ForegroundColor White
    Write-Host "  SYMBOL_PATH_EXTRA - Additional symbol paths" -ForegroundColor White
} else {
    Write-Host "❌ Some builds failed!" -ForegroundColor Red
    exit 1
}