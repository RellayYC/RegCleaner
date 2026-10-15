@echo off
rem Elevated test run for G26: whole suite as admin, log with the integrity level for check.mjs admin.
cd /d "%~dp0..\.."
if not exist artifacts\admin mkdir artifacts\admin
("%SystemRoot%\System32\whoami.exe" /groups & dotnet test RegKeeper.sln --nologo --logger "console;verbosity=normal") > artifacts\admin\run.txt 2>&1
