@echo off
setlocal
REM InkPen packaging step: copy glyph data, licenses and docs into dist.
REM Shared by build.bat (csc compiler) and test.bat (VS Build Tools chain),
REM so both compile paths produce an identical portable folder.
REM Pure ASCII only.
set "ROOT=%~dp0.."
set "OUT=%ROOT%\dist"

if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OUT%\config" mkdir "%OUT%\config"
if not exist "%OUT%\data" mkdir "%OUT%\data"
if not exist "%OUT%\data\glyphs" mkdir "%OUT%\data\glyphs"

REM local.zip is proprietary (imported from the local kdraw .gfont), machine-only.
REM Default portable build includes it for THIS machine's own use.
REM Set INKPEN_DISTRIBUTE=1 to produce a redistributable build WITHOUT local.zip.
for %%F in (mmah.zip fallback.zip latin.zip latin_script.zip pinyin.zip schema.json) do (
  copy /Y "%ROOT%\data\glyphs\%%F" "%OUT%\data\glyphs\" >nul
)
if "%INKPEN_DISTRIBUTE%"=="1" (
  if exist "%OUT%\data\glyphs\local.zip" del /Q "%OUT%\data\glyphs\local.zip"
  if exist "%OUT%\data\glyphs\local_report.json" del /Q "%OUT%\data\glyphs\local_report.json"
  echo   [dist] local.zip excluded - redistributable build
) else (
  if exist "%ROOT%\data\glyphs\local.zip" (
    copy /Y "%ROOT%\data\glyphs\local.zip" "%OUT%\data\glyphs\" >nul
    copy /Y "%ROOT%\data\glyphs\local_report.json" "%OUT%\data\glyphs\" >nul 2>nul
    echo   [local] local.zip included FOR THIS MACHINE ONLY - do not redistribute
  )
)
xcopy /Y /I /Q "%ROOT%\data\templates\*" "%OUT%\data\templates\" >nul 2>nul
xcopy /Y /I /Q "%ROOT%\data\presets\*" "%OUT%\data\presets\" >nul 2>nul
xcopy /Y /I /Q "%ROOT%\LICENSES\*" "%OUT%\LICENSES\" >nul
copy /Y "%ROOT%\README.md" "%OUT%\README.md" >nul
copy /Y "%ROOT%\NOTICE.md" "%OUT%\NOTICE.md" >nul
copy /Y "%ROOT%\build\version.txt" "%OUT%\version.txt" >nul

exit /b 0
