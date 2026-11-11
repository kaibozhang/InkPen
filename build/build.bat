@echo off
setlocal
REM InkPen one-click build with the .NET Framework built-in compiler.
REM No Visual Studio, no NuGet, no external DLL required.
REM Double-click this file, or run non-interactively with INKPEN_CI=1.
REM Packaging (glyph data / licenses / docs) lives in pack.bat, shared with test.bat
REM so the csc chain and the VS Build Tools chain produce the same portable folder.
set "ROOT=%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] C# compiler not found: %CSC%
  echo         Install .NET Framework 4.8 or VS Build Tools 2022.
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)
set "OUT=%ROOT%\dist"

echo [1/3] Compiling InkPen.exe ...
REM rsp now uses relative paths so the chain works from any folder.
REM csc resolves them against the current directory, so pushd to ROOT first.
pushd "%ROOT%"
"%CSC%" @"%ROOT%\build\sources.rsp"
set "RC=%errorlevel%"
popd
if not "%RC%"=="0" (
  echo [ERROR] Compile failed.
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)

echo [2/3] Packaging data and licenses ...
call "%ROOT%\build\pack.bat"
if errorlevel 1 (
  echo [ERROR] Packaging failed.
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)

echo [3/3] Done.
echo Output: %OUT%\InkPen.exe
if not "%INKPEN_CI%"=="1" pause
exit /b 0
