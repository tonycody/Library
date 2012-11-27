set BATDIR=%~dp0
cd %BATDIR%

call Define.bat on "Library.Security\Signature\ECDsaP521_Sha512.cs" MONO
call Define.bat on "Library.Net.Upnp\UpnpClient.cs" MONO
call Define.bat on "Library.Net.Connection\KeyExchange\ECDiffieHellmanP521_Sha512.cs" MONO
call Define.bat on "Library.Net.Lair\ConnectionsManager.cs" MONO
call Define.bat on "Library.Net.Amoeba\ConnectionsManager.cs" MONO
call Define.bat on "Library.Net.Amoeba\Cache\CacheManager.cs" MONO