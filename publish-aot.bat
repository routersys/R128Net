@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

set "VSINSTALLER=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"
if exist "%VSINSTALLER%\vswhere.exe" (
  where vswhere.exe >nul 2>&1 || set "PATH=%VSINSTALLER%;%PATH%"
)

set "RUNTIME=%~1"
if "%RUNTIME%"=="" set "RUNTIME=win-x64"

dotnet publish R128Net.Examples\R128Net.Examples.csproj -c Release -r %RUNTIME% || exit /b 1

endlocal
