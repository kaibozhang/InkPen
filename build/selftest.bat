@echo off
setlocal
REM InkPen GUI-free offline self-test: compile + run. Pure ASCII only.
set "ROOT=%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] C# compiler not found: %CSC%
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)
set "OUT=%ROOT%\dist"
if not exist "%OUT%" mkdir "%OUT%"

echo [1/2] Compiling InkPen.SelfTest.exe ...
REM same as build.bat: rsp paths are relative, so compile from the project root
pushd "%ROOT%"
"%CSC%" @"%ROOT%\build\selftest.rsp"
set "RC=%errorlevel%"
popd
if not "%RC%"=="0" (
  echo [ERROR] Compile failed.
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)

echo [2/2] Running InkPen.SelfTest.exe ...
"%OUT%\InkPen.SelfTest.exe"
set "RC=%errorlevel%"
if not "%RC%"=="0" (
  echo [ERROR] Self-test FAILED with exit code %RC%.
  if not "%INKPEN_CI%"=="1" pause
  exit /b %RC%
)
echo [OK] Self-test passed.
if not "%INKPEN_CI%"=="1" pause
exit /b 0
