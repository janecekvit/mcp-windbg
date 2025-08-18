# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repository contains both a legacy PowerShell script and a modern C# MCP (Model Context Protocol) server for interactive Windows memory dump analysis using Microsoft's Command Line Debugger (cdb.exe) or WinDbg.

## Core Components

- **CdbMcpServer** (C# .NET 8): Modern MCP server for interactive debugging
  - Persistent CDB sessions for each dump file
  - Automatic debugger detection (Windows SDK, WinDbg Store App)
  - 8 MCP tools for comprehensive dump analysis
  - 10 predefined analysis types (basic, exception, threads, heap, etc.)
  - Single-file executable deployment
  
- **cdb.ps1**: Legacy PowerShell script for batch dump analysis
  - Configures symbol paths and caching
  - Executes standardized debugging commands  
  - Generates comprehensive analysis reports

## Usage Commands

### Building and Running the MCP Server
```powershell
# Build single-file executable (recommended)
.\Scripts\Publish.ps1

# Run the MCP server
.\publish\McpProxy.exe

# Development build
dotnet build
dotnet run
```

### MCP Tools Usage Examples
```json
// Detect available debuggers
{"method": "tools/call", "params": {"name": "detect_debuggers", "arguments": {}}}

// Load dump and create session
{"method": "tools/call", "params": {"name": "load_dump", "arguments": {"dump_file_path": "C:\\dumps\\crash.dmp"}}}

// Run basic analysis (equivalent to original PowerShell script)
{"method": "tools/call", "params": {"name": "basic_analysis", "arguments": {"session_id": "abc12345"}}}

// Execute custom CDB command
{"method": "tools/call", "params": {"name": "execute_command", "arguments": {"session_id": "abc12345", "command": "!heap -s"}}}

// Run specialized analysis
{"method": "tools/call", "params": {"name": "predefined_analysis", "arguments": {"session_id": "abc12345", "analysis_type": "heap"}}}
```

## Architecture Notes

### Symbol Resolution Strategy
- Primary: Microsoft public symbol server with local caching
- Secondary: Optional custom symbol paths for private symbols
- Automatic symbol cache management in configurable directory

### Analysis Workflow
The script executes a standardized sequence of WinDbg commands:
1. Symbol configuration and reload
2. Basic process information extraction (!peb, version, threads)
3. Exception context analysis (.ecxr)
4. Automated crash analysis (!analyze -v)
5. Complete thread stack analysis (~* kb)

### Output Management
- Main analysis report written to specified output file
- Separate error log file created if cdb.exe encounters issues
- UTF-8 encoding for proper character support

## Dependencies

- Windows SDK Debuggers (cdb.exe) - typically at `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe`
- PowerShell 5.0+ with .NET System.Diagnostics.Process support
- Internet connectivity for Microsoft symbol server access
- Sufficient disk space for symbol caching

## Development Considerations

When modifying this tool:
- Maintain backward compatibility with existing dump file formats
- Test symbol resolution with both public and private symbol scenarios
- Validate output encoding to preserve text in reports
- Consider timeout mechanisms for long-running analyses
- Ensure proper error handling for corrupted dump files