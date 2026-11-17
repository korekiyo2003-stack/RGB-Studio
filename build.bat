@echo off
pushd "%~dp0scripts"
call "%~dp0scripts\build.bat"
set RC=%ERRORLEVEL%
popd
exit /b %RC%
