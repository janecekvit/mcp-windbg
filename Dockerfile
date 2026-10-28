# escape=`
# Pinned by digest (the ltsc2022 manifest list); Dependabot proposes the monthly Windows updates.
FROM mcr.microsoft.com/windows/servercore:ltsc2022@sha256:b52799a67fa596d4bddcd82ae1dab0077a84767fbc33c7342b72b53ecc71ad09

LABEL org.opencontainers.image.source="https://github.com/janecekvit/mcp-windbg" `
      org.opencontainers.image.licenses="Apache-2.0"

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]

# Install Windows Debugging Tools (cdb.exe + symsrv) via the Windows SDK web setup, pinned to
# one SDK release: the versioned URL that https://go.microsoft.com/fwlink/?linkid=2237387
# resolved to, and that file's SHA-256. The build also requires a valid Microsoft Authenticode
# signature. To move to a newer SDK, resolve the fwlink, download the file and update both values
# (Get-FileHash winsdksetup.exe -Algorithm SHA256).
RUN $url = 'https://download.microsoft.com/download/b/8/5/b85bd06f-491c-4c1c-923e-75ce2fe2378e/windowssdk/winsdksetup.exe' ; `
    $sha256 = '1BF94F7D2CF44CDF50124C3B5745A27B517367435F1BD5D0F127A33B2BD8344C' ; `
    Invoke-WebRequest -Uri $url -OutFile winsdksetup.exe -UseBasicParsing ; `
    $actual = (Get-FileHash winsdksetup.exe -Algorithm SHA256).Hash ; `
    if ($actual -ne $sha256) { throw "winsdksetup.exe SHA-256 is $actual, expected $sha256" } ; `
    $signature = Get-AuthenticodeSignature winsdksetup.exe ; `
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { `
        throw "winsdksetup.exe is not validly signed by Microsoft: $($signature.Status) $($signature.SignerCertificate.Subject)" `
    } ; `
    Start-Process -Wait winsdksetup.exe `
        -ArgumentList '/quiet','/features','OptionId.WindowsDesktopDebuggers' ; `
    Remove-Item winsdksetup.exe

# Windows base images set PATH via the registry, not via Dockerfile ENV, so
# `${PATH}` does NOT expand here — using it would clobber System32 out of PATH
# and break `powershell`, `where`, etc. List the base-image defaults explicitly.
ENV PATH="C:\Program Files (x86)\Windows Kits\10\Debuggers\x64;C:\Windows\system32;C:\Windows;C:\Windows\System32\Wbem;C:\Windows\System32\WindowsPowerShell\v1.0\;C:\Users\ContainerAdministrator\AppData\Local\Microsoft\WindowsApps"

WORKDIR /app
COPY publish/win-x64/ ./
COPY LICENSE NOTICE ./

EXPOSE 7997

# Bind on all interfaces so the published port is reachable from the host,
# another machine, or Azure. WebApplication reads ASPNETCORE_URLS natively;
# Program.cs only falls back to a loopback URL when this is unset.
ENV ASPNETCORE_URLS=http://+:7997

# appsettings.json only accepts localhost Host headers (DNS-rebinding protection for
# local runs). A container is reached by other host names, so accept any Host here and
# protect the port with an API key instead: -e Security__ApiKey=<secret>, sent by
# clients as the X-Api-Key header. Without a key the service logs a startup warning.
ENV AllowedHosts=*

# Fixed, mount-friendly symbol-cache default. Persistence is provided by the
# mount, not the app: -v C:\hostsymbols:C:\symbols locally, or an Azure Files
# share mounted on C:\symbols shared across autoscaled replicas. Override with
# -e Debugger__DefaultSymbolCache=... or the per-request X-Symbol-Cache header.
ENV Debugger__DefaultSymbolCache=C:\symbols

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 `
  CMD powershell -Command "try { Invoke-WebRequest http://localhost:7997/api/diagnostics/health -UseBasicParsing | Out-Null; exit 0 } catch { exit 1 }"

# All configuration is via environment variables
# so, image works locally and in Azure.
ENTRYPOINT ["DumpAnalysisService.exe"]
