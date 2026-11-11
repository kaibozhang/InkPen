@echo off
setlocal
REM InkPen: build with VS2022 Build Tools, then run unit tests with vstest.
REM Pure ASCII only (cmd uses the OEM code page; Chinese here would break parsing).
set "ROOT=%~dp0.."
set "VS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
set "CONF=Release"

if not exist "%VS%\MSBuild\Current\Bin\MSBuild.exe" (
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if exist "%VSWHERE%" (
    for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VS=%%i"
  )
)
if not exist "%VS%\MSBuild\Current\Bin\MSBuild.exe" (
  echo [ERROR] MSBuild not found.
  echo         Install: winget install Microsoft.VisualStudio.2022.BuildTools
  if not "%INKPEN_CI%"=="1" pause
  exit /b 1
)

set "MSB=%VS%\MSBuild\Current\Bin\MSBuild.exe"
set "VSTEST=%VS%\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"

echo [1/4] Restore packages ...
"%MSB%" "%ROOT%\InkPen.sln" /t:Restore /p:Configuration=%CONF% /nologo /v:m
if errorlevel 1 goto fail

echo [2/4] Build solution ...
"%MSB%" "%ROOT%\InkPen.sln" /t:Rebuild /p:Configuration=%CONF% "/p:Platform=Any CPU" /nologo /v:m
if errorlevel 1 goto fail

echo [3/4] Copy artifacts to dist ...
if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"
copy /Y "%ROOT%\src\App\bin\%CONF%\InkPen.exe" "%ROOT%\dist\InkPen.exe" >nul
if errorlevel 1 goto fail
call "%ROOT%\build\pack.bat"
if errorlevel 1 goto fail

echo [4/4] Run unit tests ...
"%VSTEST%" "%ROOT%\tests\InkPen.Tests\bin\%CONF%\InkPen.Tests.dll" /Logger:console
if errorlevel 1 goto fail

echo [OK] Build and unit tests passed.
if not "%INKPEN_CI%"=="1" pause
exit /b 0

:fail
echo [ERROR] Step failed. See the output above.
if not "%INKPEN_CI%"=="1" pause
exit /b 1
