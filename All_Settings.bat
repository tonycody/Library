set BATDIR=%~dp0
cd %BATDIR%
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "settings" "..\Outopos\Outopos\Properties\Settings.cs"
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "settings" "..\Amoeba\Amoeba\Properties\Settings.cs"
