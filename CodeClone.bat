set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tools\bin\Debug\Library.Tools.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" CodeClone_Amoeba-Outopos_TargetList.txt CodeClone_Amoeba-Outopos_WordList.txt
