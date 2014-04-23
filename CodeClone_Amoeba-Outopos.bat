set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tool\bin\Debug\Library.Tool.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" CodeClone_Amoeba-Outopos_TargetList.txt CodeClone_Amoeba-Outopos_WordList.txt