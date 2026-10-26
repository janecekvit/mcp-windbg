# Code review instructions for mcp-windbg

C# (.NET 10) MCP server and REST API that analyses Windows memory dumps through cdb.exe.
The service runs untrusted dump files and takes debugger commands from MCP clients, so review
for security first. Flag these as blocking:

## Security (always blocking)

- **cdb command injection**: user text must reach cdb.exe only through
  `SessionManagerService.ExecuteCommandAsync`, which applies `CdbCommandPolicy` (single line, no
  `q`/`qq`, no `.shell`/`.load`/`.create`/`.writemem`/`$<`). Block new paths that send user or
  tool-argument text to cdb without that policy, or that build a command line by string
  concatenation (`CdbProcessManager.CreateStartInfo` must use `ProcessStartInfo.ArgumentList`).
- **New endpoint or MCP tool** without `[Authorize(Policy = DumpPolicies.Read|Analyze|Admin)]`, or
  taking a session or job id without `EnsureSessionAccess` / `CanAccessJob`.
- **Caller identity** in an MCP tool taken from `IHttpContextAccessor` or a `RequestContext`
  parameter instead of `McpCallerContext`. With authentication on, a missing principal must be
  `Caller.Unidentified`, never unrestricted.
- **Dump and file paths** not checked by `DumpPathPolicy` (`Security:AllowedDumpDirectories`);
  client symbol values not validated by `SymbolInputValidator`.
- **Minidump parsing** (`Infrastructure/Minidump/`): every offset, size and count read from the file
  must be bounds-checked; malformed input raises `InvalidDataException`, never an out-of-range read,
  an unbounded allocation or an endless loop.
- Secrets, tokens or API keys in code, logs or tests; constant-time comparison for the API key.
- Links to Claude sessions (`claude.ai/code/session_…`) or other private URLs anywhere in the
  change, its commit messages or the pull request description.

## Workflows (`.github/workflows/`)

- Every `uses:` pinned to a full commit SHA with the release in a comment (`@<sha> # vX.Y.Z`).
- `persist-credentials: false` on checkout; top-level `permissions: contents: read`, with extra
  job permissions only where needed and commented.
- No `${{ }}` expansion inside `run:` scripts; pass values through `env:`.

## Correctness

- Services throw specific exceptions (`FileNotFoundException`, `ArgumentException`,
  `InvalidOperationException`); controllers map them to 400/404/500. No boolean error returns.
- cdb commands go through the per-session semaphore; never two commands on one session at once.
- Every long operation is a job (`JobManagerService.CreateJob`), never a blocking call in an
  endpoint or tool.
- A change to a component, boundary or connection updates the Mermaid flowchart in
  `docs/architecture/README.md` (no D2, ASCII art or generated images).

## Tests

- Behaviour changes need tests in `DumpAnalysisService.Tests` (or `Shared.Tests`); security
  checks need a test that the refused case is refused.
- Never skip, disable or quarantine a test to make CI pass.

Do not block on style that `dotnet format` or the analyzers would catch, or on naming
preferences. Comments, names and log messages are in English.
