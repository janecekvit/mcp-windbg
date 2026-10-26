# Architecture

The flowchart below is the source of truth for the components, their connections and the trust and
process boundaries. It is Mermaid with the ELK layout, which GitHub renders inline: edit it here, there
is no generated file. The `%%` comments carry the details the picture leaves out.

Flows over time are drawn further down as Mermaid sequence diagrams.

## Components and boundaries

```mermaid
---
config:
  layout: elk
---
flowchart TB
    %% One colour per layer: clients (indigo), authenticated entry points (green), services (blue),
    %% the debugger process (amber), files (violet), external services (grey), boundaries (slate).
    classDef client fill:#E0E7FF,stroke:#4F46E5,color:#312E81
    classDef entry fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef service fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A
    classDef debugger fill:#FEF3C7,stroke:#D97706,color:#78350F
    classDef file fill:#F3E8FF,stroke:#9333EA,color:#581C87
    classDef external fill:#F3F4F6,stroke:#6B7280,color:#374151

    %% Outside the service's trust boundary. Every request carries a JWT bearer token,
    %% an API key, or nothing when no credential is configured.
    subgraph clients["Clients"]
        %% Claude Code or any MCP client (Streamable HTTP).
        mcp_client["MCP client"]:::client
        scripts["PowerShell / Azure Function"]:::client
        cli["CommandLineClient"]:::client
    end

    %% Optional: Microsoft Entra ID or any OpenID Connect provider (Security:Jwt).
    %% Clients get access tokens from it; the service only fetches its signing keys.
    idp(["Identity provider"]):::external

    subgraph host["Windows host or Docker container (servercore ltsc2022)"]
        %% One ASP.NET Core process: MCP and REST share the same services and jobs.
        subgraph service["DumpAnalysisService process (port 7997)"]
            %% Authentication (policy scheme: JWT bearer or API key), then
            %% [Authorize(Policy = DumpPolicies.*)], then the ownership check
            %% (EnsureSessionAccess / CanAccessJob). Only GET /api/diagnostics/health is anonymous.
            subgraph entry["Authenticated entry points"]
                %% DebuggerTools. The caller comes from McpCallerContext (call-tool filter).
                mcp["MCP /mcp"]:::entry
                %% JobsController, CrashGroupsController, DiagnosticsController.
                rest["REST /api"]:::entry
                %% ProgressHub: clients subscribe to the jobs they own.
                hub["SignalR /hubs/progress"]:::entry
            end
            %% Every operation is a job: created at once, run in a background task, polled.
            jobs["JobManagerService"]:::service
            %% One CdbSessionService per session. User commands pass CdbCommandPolicy.
            sessions["SessionManagerService"]:::service
            %% MinidumpParser reads the dump's streams directly, without the debugger.
            triage["DumpTriageService"]:::service
            %% Exception code from triage plus the faulting stack from cdb.
            fingerprints["CrashFingerprintService"]:::service
        end
        %% Started with -noshell; commands framed by .echo start and end markers.
        cdb[["cdb.exe (one per dump)"]]:::debugger
        subgraph files["Files"]
            %% Restricted by Security:AllowedDumpDirectories.
            dumps[/"Dump files"/]:::file
            symcache[("Symbol cache")]:::file
            %% Crash groups (Fingerprint:RegistryPath).
            registry[/"crash-groups.json"/]:::file
        end
    end

    %% Microsoft and NuGet symbol servers, plus any configured srv* entries.
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

    %% Boundaries; the clients boundary is dashed (outside the trust boundary).
    style clients fill:#EEF2FF,stroke:#6366F1,stroke-dasharray:3 3
    style host fill:#F8FAFC,stroke:#475569
    style service fill:#EFF6FF,stroke:#3B82F6
    style entry fill:#ECFDF5,stroke:#10B981
    style files fill:#FAF5FF,stroke:#A855F7
    %% Edges in neutral slate, so the colours mark the layers.
    linkStyle default stroke:#475569
```

## Loading a dump (`load_dump`)

Every operation is a job. The MCP tool creates it at once, runs the work in a background task and
polls the job every second, forwarding progress to the client as MCP progress notifications. REST
clients get the job id back (202 Accepted) and follow it over SignalR or `GET /api/jobs/{id}`.

```mermaid
sequenceDiagram
    autonumber
    participant Client as MCP client
    participant Tool as DebuggerTools (load_dump)
    participant Jobs as JobManagerService
    participant Sessions as SessionManagerService
    participant Cdb as CdbSessionService
    participant Proc as cdb.exe
    participant Hub as ProgressHub (SignalR)

    Client->>Tool: tools/call load_dump(dump_file_path)
    Tool->>Jobs: CreateJob(LoadDump, owner = caller id)
    Tool-)Sessions: background task: CreateSessionWithDumpAsync(jobId, path, symbols, owner)
    Sessions->>Sessions: file exists, AllowedDumpDirectories, MaxConcurrentSessions
    Sessions->>Cdb: LoadDumpAsync(path, progress)
    Cdb->>Proc: start cdb.exe -noshell -z dump -y symbol path
    loop symbol setup and .reload (can take minutes)
        Cdb->>Proc: .echo start marker, command, .echo end marker
        Proc-->>Cdb: output up to the end marker
        Cdb-)Jobs: UpdateProgressAsync(phase, progress, message)
        Jobs-)Hub: Progress to the job's group
    end
    loop every second until the job ends
        Tool->>Jobs: GetJobStatus(jobId)
        Tool-->>Client: notifications/progress
    end
    Sessions->>Jobs: CompleteJobAsync(jobId, sessionId)
    Jobs-)Hub: Completed
    Tool-->>Client: result: session id
```

## Securing a request

Every REST, MCP and SignalR request takes the same path. With no credential configured, every caller
is unrestricted and the ownership check passes. `GET /api/diagnostics/health` is the one anonymous route.

```mermaid
sequenceDiagram
    autonumber
    participant Client
    participant Scheme as Policy scheme
    participant Jwt as JwtBearer
    participant Key as ApiKeyAuthenticationHandler
    participant Policy as DumpPolicies
    participant Entry as Endpoint or MCP tool
    participant Owner as Ownership

    Client->>Scheme: request with a bearer token or X-Api-Key
    alt bearer token shaped like a JWT, and Security:Jwt is configured
        Scheme->>Jwt: validate signature, issuer, audience, lifetime
        Jwt-->>Scheme: principal with roles (Jwt:RoleClaimTypes)
    else anything else
        Scheme->>Key: constant-time compare with Security:ApiKey
        Key-->>Scheme: administrator principal
    end
    Scheme->>Policy: [Authorize(Policy = Read / Analyze / Admin)]
    alt role too low or no identity
        Policy-->>Client: 401 (with resource_metadata for OAuth), 403, or an MCP tool error
    else allowed
        Policy->>Entry: run, with Caller.From(user)
        Entry->>Owner: EnsureSessionAccess / CanAccessJob
        alt another user's session or job
            Owner-->>Client: not found
        else own, or an admin or the API key
            Entry->>Entry: AuditLog.Record, create the job
            Entry-->>Client: 202 Accepted / tool result
        end
    end
```
