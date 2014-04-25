set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tools\bin\Debug\Library.Tools.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" CodeClone_Outopos-Amoeba_TargetList.txt CodeClone_Outopos-Amoeba_WordList.txt