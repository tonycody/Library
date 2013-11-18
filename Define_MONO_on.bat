set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tool\bin\Debug\Library.Tool.exe"
IF EXIST %TOOL% call %TOOL% define on "Define_MONO.txt" MONO
