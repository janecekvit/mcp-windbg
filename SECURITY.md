# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub Security Advisories:
[Report a vulnerability](https://github.com/janecekvit/mcp-windbg/security/advisories/new).
Do not open a public issue, pull request or discussion for a suspected vulnerability.

Include what you can of:

- the affected version or commit, and how the service was deployed (single-file executable, Docker image, settings);
- steps or a proof of concept (for a malicious dump, the smallest file that shows it, or how to build one);
- the impact you expect: what an attacker controls and what they gain.

## What to expect

This is a project with a single maintainer, so the targets are best effort:

| Step | Target |
|---|---|
| Acknowledge the report | 5 working days |
| First assessment (confirmed or not, severity) | 14 days |
| Fix released for a confirmed High or Critical issue | 30 days |

You will be kept informed and credited in the advisory, unless you prefer otherwise.

## Supported versions

Fixes go into the latest release and the `main` branch. Older releases are not patched; upgrade to the latest.

## Scope

In scope, in the service (`DumpAnalysisService`), the client (`CommandLineClient`), the Docker image and this repository's workflows:

- **Untrusted dump files.** A crafted `.dmp` that crashes, hangs or exhausts the service in `triage_dump` (our own minidump parser) or in the code that drives cdb, or that reads or writes outside the dump.
- **Command injection into cdb/WinDbg.** MCP tool arguments or REST input that run debugger commands the policy blocks (`.shell`, `.load`, `.create`, `.writemem`, `$<` and the rest of `CdbCommandPolicy`), or that add commands to cdb.exe's input outside the start/end markers.
- **Path traversal.** Reaching dump files, symbol paths or the crash group registry outside the configured locations (`Security:AllowedDumpDirectories`, symbol validation).
- **Authentication and authorization bypass.** Calling a tool or endpoint without a valid API key or JWT, with a role below the one it requires, or reading or using another user's session or job.
- **Denial of service from large or many dumps.** Unbounded memory, disk or time from one request, or getting around `MaxConcurrentSessions`.
- **Supply chain.** The release ZIP, the `ghcr.io/janecekvit/mcp-windbg` image, the GitHub Actions workflows and the dependencies they pull in.

Out of scope:

- vulnerabilities in cdb.exe, WinDbg or Windows itself: report them to the [Microsoft Security Response Center](https://msrc.microsoft.com/);
- deployments that turn protections off on purpose (`AllowUnsafeCommands`, no credential on a port reachable from other machines, `AllowedHosts=*` without an API key or JWT);
- findings that need an attacker who already controls the host or the service's configuration.
