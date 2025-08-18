param(
    [Parameter(Mandatory=$true)]
    [string]$DumpFile,                                # Path to single .dmp file

    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "$PSScriptRoot\report.txt", # Where to save report

    [Parameter(Mandatory=$false)]
    [string]$CdbPath = "C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",

    [Parameter(Mandatory=$false)]
    [string]$SymbolCache = "C:\symbols",              # Local symbol cache

    [Parameter(Mandatory=$false)]
    [string]$SymbolPathExtra = ""                     # Additional paths: e.g. "C:\MyPdbs;srv*\\server\symbols"
)

# Input validation
if (!(Test-Path $DumpFile)) {
    Write-Error "Dump not found: $DumpFile"
    exit 1
}
if (!(Test-Path $CdbPath)) {
    Write-Error "cdb.exe not found at $CdbPath. Adjust -CdbPath parameter."
    exit 1
}

# Prepare cache directory
New-Item -ItemType Directory -Force -Path $SymbolCache | Out-Null

# Build symbol path: Extra (if any) + MS server with cache
$msSrv = "srv*$SymbolCache*https://msdl.microsoft.com/download/symbols"
$symbolPath = if ($SymbolPathExtra -and $SymbolPathExtra.Trim()) { "$SymbolPathExtra;$msSrv" } else { $msSrv }

# Commands for cdb
$cmd = @(
    ".symfix $SymbolCache",
    ".symopt+ 0x40",
    ".reload",
    ".echo ---------- BASIC INFO ----------",
    "version",
    "!peb",
    "~",
    ".echo ---------- EXCEPTION CONTEXT ----------",
    ".ecxr",
    ".echo ---------- ANALYZE ----------",
    "!analyze -v",
    ".echo ---------- ALL THREAD STACKS ----------",
    "~* kb",
    ".echo ---------- FINAL ----------",
    "q"
) -join "; "

Write-Host "Processing dump: $DumpFile"
Write-Host "Report to: $OutputFile"
Write-Host "SymbolPath: $symbolPath"

# Running cdb
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $CdbPath
$psi.Arguments = "-z `"$DumpFile`" -y `"$symbolPath`" -c `"$cmd`" -logo `"$OutputFile`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
$p.WaitForExit()

if ($p.ExitCode -ne 0) {
    Write-Warning "cdb exit code: $($p.ExitCode)"
}
if ($stderr) {
    $errPath = [System.IO.Path]::ChangeExtension($OutputFile, ".error.txt")
    $stderr | Out-File -Encoding UTF8 $errPath
    Write-Warning "Errors written to: $errPath"
}
