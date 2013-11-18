set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library\Library.Tool\bin\Debug\Library.Tool.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" CodeClone_Amoeba-Lair_CloneList.txt CodeClone_Amoeba-Lair_WordList.txt