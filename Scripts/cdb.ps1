param(
    [Parameter(Mandatory=$true)]
    [string]$DumpFile,                                # Cesta k jednomu .dmp

    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "$PSScriptRoot\report.txt", # Kam uložit report

    [Parameter(Mandatory=$false)]
    [string]$CdbPath = "C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",

    [Parameter(Mandatory=$false)]
    [string]$SymbolCache = "C:\symbols",              # Lokální cache symbolů

    [Parameter(Mandatory=$false)]
    [string]$SymbolPathExtra = ""                     # Další cesty: např. "C:\MyPdbs;srv*\\server\symbols"
)

# Ověření vstupů
if (!(Test-Path $DumpFile)) {
    Write-Error "Dump nenalezen: $DumpFile"
    exit 1
}
if (!(Test-Path $CdbPath)) {
    Write-Error "Nenalezen cdb.exe na $CdbPath. Uprav parametr -CdbPath."
    exit 1
}

# Připrav cache složku
New-Item -ItemType Directory -Force -Path $SymbolCache | Out-Null

# Sestav symbol path: Extra (pokud je) + MS server s cache
$msSrv = "srv*$SymbolCache*https://msdl.microsoft.com/download/symbols"
$symbolPath = if ($SymbolPathExtra -and $SymbolPathExtra.Trim()) { "$SymbolPathExtra;$msSrv" } else { $msSrv }

# Příkazy pro cdb
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

Write-Host "Zpracovávám dump: $DumpFile"
Write-Host "Report do: $OutputFile"
Write-Host "SymbolPath: $symbolPath"

# Spuštění cdb
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
    Write-Warning "Chyby zapsány do: $errPath"
}
