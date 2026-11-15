@echo off
rem Gives cmd.exe a Commodore 64 style prompt: READY. on its own line, the cursor below it.
rem C64 Console does this by itself; use this script in other terminals, or run it from AutoRun:
rem   reg add "HKCU\Software\Microsoft\Command Processor" /v AutoRun /d "%~f0" /f
prompt READY.$_
