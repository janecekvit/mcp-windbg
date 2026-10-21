# Architecture

![mcp-windbg architecture](architecture.svg)

The diagram is rendered from [`architecture.d2`](architecture.d2), which is the source of truth for the
components, their connections and the trust and process boundaries. To change it, edit the `.d2`, run
`Scripts/render-diagrams.sh` and commit both files. CI fails when the SVG is stale.

Flows over time are drawn below as Mermaid sequence diagrams, which GitHub renders inline.

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
