# CDB MCP Server

MCP (Model Context Protocol) server pro interaktivní debugging Windows memory dump souborů pomocí Microsoft Command Line Debugger (cdb.exe).

## Funkce

- **Interaktivní debugging**: Persistentní CDB sessions pro každý dump soubor
- **Předpřipravené analýzy**: 10 typů specializovaných analýz (basic, exception, threads, heap, atd.)
- **Custom příkazy**: Možnost vkládat vlastní WinDbg/CDB příkazy
- **Správa sessions**: Současná práce s více dump soubory
- **MCP kompatibilita**: Standardní MCP protokol pro integraci

## Požadavky

- .NET 8.0+
- Windows SDK Debuggers (cdb.exe)
- Přístup k Microsoft symbol serveru (pro downloading symbolů)

## Instalace

### Rychlá instalace (Single-file executable)
```powershell
# Build single-file executable
.\publish.ps1

# Spustit
.\publish\CdbMcpServer.exe
```

### Development build
```bash
dotnet build
dotnet run
```

## Automatická detekce debuggeru

Server automaticky detekuje dostupné CDB/WinDbg instalace:

- **Windows SDK** (preferováno): `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe`
- **WinDbg Store App**: `C:\Program Files\WindowsApps\Microsoft.WinDbg_*\amd64\windbg.exe`
- **Různé architektury**: x64, x86, amd64

Použijte tool `detect_debuggers` pro zjištění dostupných instalací.

## Konfigurace

Server se konfiguruje pomocí environment variables (volitelné):

- `CDB_PATH`: Vlastní cesta k cdb.exe/windbg.exe (přepíše auto-detekci)
- `SYMBOL_CACHE`: Lokální cache pro symboly (default: `%LOCALAPPDATA%\CdbMcpServer\symbols`)
- `SYMBOL_PATH_EXTRA`: Dodatečné symbol paths

## MCP Tools

### load_dump
Načte memory dump a vytvoří novou debugging session.

**Parametry:**
- `dump_file_path`: Cesta k .dmp souboru

### execute_command
Vykoná WinDbg/CDB příkaz v existující session.

**Parametry:**
- `session_id`: ID debugging session
- `command`: Příkaz k vykonání (např. "kb", "!analyze -v")

### predefined_analysis
Spustí předpřipravenou analýzu.

**Parametry:**
- `session_id`: ID debugging session
- `analysis_type`: Typ analýzy (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)

### basic_analysis
Spustí základní analýzu (ekvivalent PowerShell skriptu).

**Parametry:**
- `session_id`: ID debugging session

### list_sessions
Vypíše všechny aktivní debugging sessions.

### list_analyses
Vypíše všechny dostupné předpřipravené analýzy s popisem.

### detect_debuggers
Detekuje dostupné CDB/WinDbg instalace na systému.

### close_session
Ukončí debugging session a uvolní prostředky.

**Parametry:**
- `session_id`: ID debugging session k ukončení

## Předpřipravené analýzy

1. **basic** - Kompletní základní analýza (exception context, analyze -v, thread stacks)
2. **exception** - Detailní analýza exception s exception a context records
3. **threads** - Kompletní analýza threadů včetně informací a stacků
4. **heap** - Analýza heap včetně statistik a validace
5. **modules** - Analýza modulů (loaded, detailed, unloaded)
6. **handles** - Analýza handles včetně process handles
7. **locks** - Analýza critical sections a deadlock detekce
8. **memory** - Analýza virtuální paměti a address space
9. **drivers** - Analýza ovladačů a device objects
10. **processes** - Analýza procesů a process tree

## Integrace s Claude Code

### 1. Sestavení a instalace
```powershell
# Sestavit single-file executable
.\publish.ps1
# nebo
dotnet publish CdbMcpServer.csproj -c Release -r win-x64 -o publish --self-contained true -p:PublishSingleFile=true
```

### 2. Konfigurace Claude Code

Přidejte do `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcp": {
    "servers": {
      "cdb-debugging": {
        "command": "D:\\MCP2\\publish\\CdbMcpServer.exe",
        "args": []
      }
    }
  }
}
```

### 3. Použití v Claude Code

Po restartu Claude Code můžete používat:

- `detect_debuggers` - ověření konfigurace debuggerů
- `load_dump` - načtení dump souboru a vytvoření session
- `basic_analysis` - kompletní crash analýza
- `execute_command` - vlastní CDB příkazy
- `predefined_analysis` - specializované analýzy (heap, threads, modules, atd.)

**Příklad workflow:**
1. "Použij detect_debuggers k ověření konfigurace"
2. "Načti dump soubor D:\\crash.dmp pomocí load_dump"
3. "Proveď basic_analysis na session"
4. "Spusť predefined_analysis typu heap"

## Přímé MCP použití

```json
// Načíst dump
{"method": "tools/call", "params": {"name": "load_dump", "arguments": {"dump_file_path": "C:\\dumps\\crash.dmp"}}}

// Spustit základní analýzu
{"method": "tools/call", "params": {"name": "basic_analysis", "arguments": {"session_id": "abc12345"}}}

// Vlastní příkaz
{"method": "tools/call", "params": {"name": "execute_command", "arguments": {"session_id": "abc12345", "command": "!heap -s"}}}

// Specializovaná analýza
{"method": "tools/call", "params": {"name": "predefined_analysis", "arguments": {"session_id": "abc12345", "analysis_type": "heap"}}}
```