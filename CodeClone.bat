set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tool\bin\Debug\Library.Tool.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" CodeClone_CloneList.txt CodeClone_WordList.txt