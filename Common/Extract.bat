set BATDIR=%~dp0
cd %BATDIR%

set TOOL="..\Library.Tools\bin\Debug\Library.Tools.exe"
IF EXIST %TOOL% call %TOOL% "Template" Template\Settings.txt

copy /Y /B Cpp\Library\Release\Library.dll ..\Library\Assemblies\Library_x86.dll
copy /Y /B Cpp\Library\x64\Release\Library.dll ..\Library\Assemblies\Library_x64.dll

copy /Y /B Cpp\Library_Correction\Release\Library_Correction.dll ..\Library.Correction\Assemblies\Library_Correction_x86.dll
copy /Y /B Cpp\Library_Correction\x64\Release\Library_Correction.dll ..\Library.Correction\Assemblies\Library_Correction_x64.dll

copy /Y /B Cpp\Library_Security\Release\Library_Security.dll ..\Library.Security\Assemblies\Library_Security_x86.dll
copy /Y /B Cpp\Library_Security\x64\Release\Library_Security.dll ..\Library.Security\Assemblies\Library_Security_x64.dll

copy /Y /B Cpp\Hashcash\Release\Hashcash.exe ..\Library.Security\Assemblies\Hashcash_x86.exe
copy /Y /B Cpp\Hashcash\x64\Release\Hashcash.exe ..\Library.Security\Assemblies\Hashcash_x64.exe