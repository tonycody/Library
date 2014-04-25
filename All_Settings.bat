set BATDIR=%~dp0
cd %BATDIR%
call "..\Library\Library.Tools\bin\Debug\Library.Tools.exe" "settings" "..\Outopos\Outopos\Properties\Settings.cs"
call "..\Library\Library.Tools\bin\Debug\Library.Tools.exe" "settings" "..\Amoeba\Amoeba\Properties\Settings.cs"
