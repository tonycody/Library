set BATDIR=%~dp0
cd %BATDIR%
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "languages" "..\Amoeba\Amoeba\Properties\LanguagesManager.cs" "..\Amoeba\Amoeba\Languages"
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "languages" "..\Lair\Lair\Properties\LanguagesManager.cs" "..\Lair\Lair\Languages"
