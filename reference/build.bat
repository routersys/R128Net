@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

set "EBUR128_TAG=v1.2.6"
set "EBUR128_COMMIT=67b33abe1558160ed76ada1322329b0e9e058b02"

if not exist ebur128-src (
  git clone --depth 1 --branch %EBUR128_TAG% https://github.com/jiixyj/libebur128.git ebur128-src || exit /b 1
)

set "ACTUAL_COMMIT="
for /f %%i in ('git -C ebur128-src rev-parse HEAD') do set "ACTUAL_COMMIT=%%i"
if /i not "%ACTUAL_COMMIT%"=="%EBUR128_COMMIT%" (
  echo expected %EBUR128_COMMIT% but found %ACTUAL_COMMIT%
  exit /b 1
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo vswhere.exe not found.
  exit /b 1
)

set "VSPATH_FILE=%TEMP%\r128net_vspath.txt"
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath > "%VSPATH_FILE%"
set "VSPATH="
set /p VSPATH=<"%VSPATH_FILE%"
del "%VSPATH_FILE%"
if not defined VSPATH (
  echo MSVC toolset not found.
  exit /b 1
)

call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

set "OUTPUT_DIR=%~1"
if "%OUTPUT_DIR%"=="" set "OUTPUT_DIR=data"

if not exist obj mkdir obj
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

cl /nologo /O2 /fp:precise /D_USE_MATH_DEFINES /I ebur128-src\ebur128 /I ebur128-src\ebur128\queue /I . /Fo:obj\ /Fe:r128ref.exe main.c || exit /b 1

.\r128ref.exe "%OUTPUT_DIR%" || exit /b 1

endlocal
