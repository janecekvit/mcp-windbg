# Architecture in Mermaid (preview)

A preview of `architecture.d2` drawn as a Mermaid flowchart, to compare how GitHub renders it.
Same nodes, edges, colours and boundaries as the D2 source.

## ELK layout

```mermaid
---
config:
  layout: elk
---
flowchart TB
    classDef client fill:#E0E7FF,stroke:#4F46E5,color:#312E81
    classDef entry fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef service fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A
    classDef debugger fill:#FEF3C7,stroke:#D97706,color:#78350F
    classDef file fill:#F3E8FF,stroke:#9333EA,color:#581C87
    classDef external fill:#F3F4F6,stroke:#6B7280,color:#374151

    subgraph clients["Clients"]
        mcp_client["MCP client"]:::client
        scripts["PowerShell / Azure Function"]:::client
        cli["CommandLineClient"]:::client
    end

    idp(["Identity provider"]):::external

    subgraph host["Windows host or Docker container (servercore ltsc2022)"]
        subgraph service["DumpAnalysisService process (port 7997)"]
            subgraph entry["Authenticated entry points"]
                mcp["MCP /mcp"]:::entry
                rest["REST /api"]:::entry
                hub["SignalR /hubs/progress"]:::entry
            end
            jobs["JobManagerService"]:::service
            sessions["SessionManagerService"]:::service
            triage["DumpTriageService"]:::service
            fingerprints["CrashFingerprintService"]:::service
        end
        cdb[["cdb.exe (one per dump)"]]:::debugger
        subgraph files["Files"]
            dumps[/"Dump files"/]:::file
            symcache[("Symbol cache")]:::file
            registry[/"crash-groups.json"/]:::file
        end
    end

    symsrv(["Symbol servers"]):::external

    mcp_client --> mcp
    scripts --> rest
    cli --> rest
    cli --> hub
    idp -. token .-> clients
    idp -. signing keys .-> entry

    entry -- "create, poll" --> jobs
    entry -- "load, command" --> sessions
    entry -- triage --> triage
    entry -- fingerprint --> fingerprints
    jobs -- push --> hub

    sessions -- progress --> jobs
    sessions -- "stdin / stdout" --> cdb
    fingerprints -- stack --> sessions
    fingerprints -- code --> triage
    fingerprints -- record --> registry

    triage -- streams --> dumps
    triage -- "cached?" --> symcache
    cdb -- open --> dumps
    cdb --> symcache
    cdb -- download --> symsrv

    style clients fill:#EEF2FF,stroke:#6366F1,stroke-dasharray:3 3
    style host fill:#F8FAFC,stroke:#475569
    style service fill:#EFF6FF,stroke:#3B82F6
    style entry fill:#ECFDF5,stroke:#10B981
    style files fill:#FAF5FF,stroke:#A855F7
```

## Default layout (dagre)

```mermaid
flowchart TB
    classDef client fill:#E0E7FF,stroke:#4F46E5,color:#312E81
    classDef entry fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef service fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A
    classDef debugger fill:#FEF3C7,stroke:#D97706,color:#78350F
    classDef file fill:#F3E8FF,stroke:#9333EA,color:#581C87
    classDef external fill:#F3F4F6,stroke:#6B7280,color:#374151

    subgraph clients["Clients"]
        mcp_client["MCP client"]:::client
        scripts["PowerShell / Azure Function"]:::client
        cli["CommandLineClient"]:::client
    end

    idp(["Identity provider"]):::external

    subgraph host["Windows host or Docker container (servercore ltsc2022)"]
        subgraph service["DumpAnalysisService process (port 7997)"]
            subgraph entry["Authenticated entry points"]
                mcp["MCP /mcp"]:::entry
                rest["REST /api"]:::entry
                hub["SignalR /hubs/progress"]:::entry
            end
            jobs["JobManagerService"]:::service
            sessions["SessionManagerService"]:::service
            triage["DumpTriageService"]:::service
            fingerprints["CrashFingerprintService"]:::service
        end
        cdb[["cdb.exe (one per dump)"]]:::debugger
        subgraph files["Files"]
            dumps[/"Dump files"/]:::file
            symcache[("Symbol cache")]:::file
            registry[/"crash-groups.json"/]:::file
        end
    end

    symsrv(["Symbol servers"]):::external

    mcp_client --> mcp
    scripts --> rest
    cli --> rest
    cli --> hub
    idp -. token .-> clients
    idp -. signing keys .-> entry

    entry -- "create, poll" --> jobs
    entry -- "load, command" --> sessions
    entry -- triage --> triage
    entry -- fingerprint --> fingerprints
    jobs -- push --> hub

    sessions -- progress --> jobs
    sessions -- "stdin / stdout" --> cdb
    fingerprints -- stack --> sessions
    fingerprints -- code --> triage
    fingerprints -- record --> registry

    triage -- streams --> dumps
    triage -- "cached?" --> symcache
    cdb -- open --> dumps
    cdb --> symcache
    cdb -- download --> symsrv

    style clients fill:#EEF2FF,stroke:#6366F1,stroke-dasharray:3 3
    style host fill:#F8FAFC,stroke:#475569
    style service fill:#EFF6FF,stroke:#3B82F6
    style entry fill:#ECFDF5,stroke:#10B981
    style files fill:#FAF5FF,stroke:#A855F7
```

## D2 (current)

![D2 architecture](architecture.svg)
