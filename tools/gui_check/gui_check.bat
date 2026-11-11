@echo off
rem InkPen GUI acceptance check (no human eyes). Requires Python 3 with Pillow.
rem Usage:  gui_check.bat           -> all three checks
rem         gui_check.bat smoke     -> only smoke
setlocal
set "PY=C:\Users\Administrator\AppData\Local\Programs\Python\Python312\python.exe"
if not exist "%PY%" set "PY=python"
"%PY%" "%~dp0gui_check.py" %*
exit /b %ERRORLEVEL%
