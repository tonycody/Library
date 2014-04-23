set BATDIR=%~dp0
cd %BATDIR%
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "languages" "..\Outopos\Outopos\Properties\LanguagesManager.cs" "..\Outopos\Outopos\Languages"
call "..\Library\Library.Tool\bin\Debug\Library.Tool.exe" "languages" "..\Amoeba\Amoeba\Properties\LanguagesManager.cs" "..\Amoeba\Amoeba\Languages"

