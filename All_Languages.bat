set BATDIR=%~dp0
cd %BATDIR%

call "..\Library\Library.Tools\bin\Debug\Library.Tools.exe" "languages" "..\Amoeba\Amoeba\Properties\LanguagesManager.cs" "..\Amoeba\Amoeba\Languages"
call "..\Library\Library.Tools\bin\Debug\Library.Tools.exe" "languages" "..\Outopos\Outopos\Properties\LanguagesManager.cs" "..\Outopos\Outopos\Languages"

