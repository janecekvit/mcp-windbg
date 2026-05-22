# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2022

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]

# Install Windows Debugging Tools (cdb.exe + symsrv) via Windows SDK web setup.
RUN Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/?linkid=2237387" `
        -OutFile winsdksetup.exe ; `
    Start-Process -Wait winsdksetup.exe `
        -ArgumentList '/quiet','/features','OptionId.WindowsDesktopDebuggers' ; `
    Remove-Item winsdksetup.exe

# Windows base images set PATH via the registry, not via Dockerfile ENV, so
# `${PATH}` does NOT expand here — using it would clobber System32 out of PATH
# and break `powershell`, `where`, etc. List the base-image defaults explicitly.
ENV PATH="C:\Program Files (x86)\Windows Kits\10\Debuggers\x64;C:\Windows\system32;C:\Windows;C:\Windows\System32\Wbem;C:\Windows\System32\WindowsPowerShell\v1.0\;C:\Users\ContainerAdministrator\AppData\Local\Microsoft\WindowsApps"

WORKDIR /app
COPY publish/win-x64/ ./

EXPOSE 7997
ENV ASPNETCORE_URLS=http://+:7997

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 `
  CMD powershell -Command "try { Invoke-WebRequest http://localhost:7997/api/jobs -UseBasicParsing | Out-Null; exit 0 } catch { exit 1 }"

ENTRYPOINT ["DumpAnalysisService.exe"]
