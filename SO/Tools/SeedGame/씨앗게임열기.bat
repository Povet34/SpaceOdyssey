@echo off
cd /d "%~dp0"
where python >nul 2>nul
if errorlevel 1 goto nopython
python seedgame.py build --open
if errorlevel 1 pause
goto :eof
:nopython
echo Python 3 is required. Install it from https://www.python.org/downloads/ and try again.
pause
