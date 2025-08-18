# Integrace CDB MCP Server s Claude Code

## 1. Příprava executable

Server je již zkompilován jako single-file executable:
```
D:\MCP2\publish\CdbMcpServer.exe
```

## 2. Konfigurace Claude Code

### Metoda A: Globální konfigurace

Přidejte do vašeho globálního Claude Code config souboru:

**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcp": {
    "servers": {
      "cdb-debugging": {
        "command": "D:\\MCP2\\publish\\CdbMcpServer.exe",
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

### Metoda B: Projekt-specifická konfigurace

Vytvořte `.claude/mcp_config.json` v kořenu vašeho projektu:

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

## 3. Dostupné MCP Tools

Po konfiguraci budete mít v Claude Code k dispozici:

### `detect_debuggers`
- **Popis**: Detekuje dostupné CDB/WinDbg instalace
- **Použití**: Diagnostika a ověření konfigurace

### `load_dump` 
- **Popis**: Načte memory dump a vytvoří debugging session
- **Parametry**: `dump_file_path` - cesta k .dmp souboru
- **Výstup**: Vrací session ID pro další operace

### `execute_command`
- **Popis**: Vykoná custom CDB/WinDbg příkaz
- **Parametry**: `session_id`, `command`
- **Příklady**: `"kb"`, `"!analyze -v"`, `"dt ntdll!_PEB"`

### `basic_analysis`
- **Popis**: Kompletní základní analýza (ekvivalent PowerShell skriptu)
- **Parametry**: `session_id`
- **Výstup**: Detailní crash analýza

### `predefined_analysis`
- **Popis**: Specializovaná analýza podle typu
- **Parametry**: `session_id`, `analysis_type`
- **Typy**: `basic`, `exception`, `threads`, `heap`, `modules`, `handles`, `locks`, `memory`, `drivers`, `processes`

### `list_sessions`
- **Popis**: Zobrazí všechny aktivní debugging sessions

### `list_analyses`
- **Popis**: Zobrazí dostupné typy analýz s popisem

### `close_session`
- **Popis**: Ukončí debugging session
- **Parametry**: `session_id`

## 4. Příklady použití v Claude Code

### Základní workflow:
1. `detect_debuggers` - ověření konfigurace
2. `load_dump` s cestou k dump souboru → získáte session_id  
3. `basic_analysis` pro rychlý přehled
4. `execute_command` pro specializované příkazy
5. `close_session` po dokončení

### Pokročilé použití:
- `predefined_analysis` s `analysis_type: "heap"` pro heap analýzu
- `execute_command` s `"!clrstack"` pro .NET stack traces
- `execute_command` s `"lm"` pro list modulů

## 5. Environment Variables (volitelné)

Pokud auto-detekce nefunguje, nastavte:

```json
"env": {
  "CDB_PATH": "C:\\path\\to\\your\\cdb.exe",
  "SYMBOL_CACHE": "C:\\your\\symbol\\cache",
  "SYMBOL_PATH_EXTRA": "C:\\additional\\symbols"
}
```

## 6. Restart Claude Code

Po přidání konfigurace restartujte Claude Code pro načtení MCP serveru.

## 7. Ověření funkčnosti

V Claude Code zadejte:
> "Použij detect_debuggers tool k ověření konfigurace"

Měli byste vidět informace o nalezených debuggerech a konfiguraci.