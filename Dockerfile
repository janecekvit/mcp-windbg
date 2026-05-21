# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2022

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]

# Install Windows Debugging Tools (cdb.exe + symsrv) via Windows SDK web setup.
RUN Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/?linkid=2237387" `
        -OutFile winsdksetup.exe ; `
    Start-Process -Wait winsdksetup.exe `
        -ArgumentList '/quiet','/features','OptionId.WindowsDesktopDebuggers' ; `
    Remove-Item winsdksetup.exe

ENV PATH="C:\Program Files (x86)\Windows Kits\10\Debuggers\x64;${PATH}"

WORKDIR /app
COPY publish/win-x64/ ./

EXPOSE 7997
ENV ASPNETCORE_URLS=http://+:7997

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 `
  CMD powershell -Command "try { Invoke-WebRequest http://localhost:7997/api/jobs -UseBasicParsing | Out-Null; exit 0 } catch { exit 1 }"

ENTRYPOINT ["DumpAnalysisService.exe"]
