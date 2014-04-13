set BATDIR=%~dp0
cd %BATDIR%

copy /Y /B Cpp\ReedSolomon\Release\ReedSolomon.dll ReedSolomon_x86.dll
copy /Y /B Cpp\ReedSolomon\x64\Release\ReedSolomon.dll ReedSolomon_x64.dll

copy /Y /B Cpp\ReedSolomon_Utility\Release\ReedSolomon_Utility.dll ReedSolomon_Utility_x86.dll
copy /Y /B Cpp\ReedSolomon_Utility\x64\Release\ReedSolomon_Utility.dll ReedSolomon_Utility_x64.dll

