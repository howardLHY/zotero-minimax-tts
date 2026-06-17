@echo off
setlocal

rem Build the x64 SAPI5 bridge with Visual Studio 2022 or newer Build Tools.
rem This script intentionally avoids machine-specific paths.

set "VCVARS64="

if defined VSINSTALLDIR if exist "%VSINSTALLDIR%\VC\Auxiliary\Build\vcvars64.bat" (
  set "VCVARS64=%VSINSTALLDIR%\VC\Auxiliary\Build\vcvars64.bat"
)

if defined VCVARS64 goto have_vcvars

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto missing_vcvars

for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -find VC\Auxiliary\Build\vcvars64.bat`) do set "VCVARS64=%%I"

if defined VCVARS64 goto have_vcvars
goto missing_vcvars

:have_vcvars
call "%VCVARS64%" >nul
if errorlevel 1 exit /b %errorlevel%

cl /nologo /EHsc /LD /O2 /D _WINDLL http_sapi5_engine.cpp ^
   /link /DEF:http_sapi5_engine.def ^
   winhttp.lib ole32.lib advapi32.lib oleaut32.lib
exit /b %errorlevel%

:missing_vcvars
echo Visual Studio C++ Build Tools were not found.
echo Install "Desktop development with C++" or run this from an x64 Native Tools Command Prompt.
exit /b 1
