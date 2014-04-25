set BATDIR=%~dp0
cd %BATDIR%
call "..\Library\Library.Tools\bin\Debug\Library.Tools.exe" "DigitalSignature_Verify" %1 %2
