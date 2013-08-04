using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Library.Net.Connection;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Connection")]
    public class Test_Library_Net_Connection
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();
        private const int MaxReceiveCount = 1 * 1024 * 1024;

        [Test]
        public void Test_TcpConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            var tcpClient = new TcpConnection(client.Client, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager);
            var tcpServer = new TcpConnection(server, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager);

            using (MemoryStream stream = new MemoryStream())
            {
                var buffer = new byte[1024 * 8];
                _random.NextBytes(buffer);

                stream.Write(buffer, 0, buffer.Length);
                stream.Seek(0, SeekOrigin.Begin);

                var secureClientSend = tcpClient.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                var secureServerReceive = tcpServer.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                tcpClient.EndSend(secureClientSend);
                var returnStream = tcpServer.EndReceive(secureServerReceive);

                var buff2 = new byte[(int)returnStream.Length];
                returnStream.Read(buff2, 0, buff2.Length);

                Assert.IsTrue(Collection.Equals(buffer, buff2), "TcpConnection #1");
            }

            using (MemoryStream stream = new MemoryStream())
            {
                var buffer = new byte[1024 * 8];
                _random.NextBytes(buffer);

                stream.Write(buffer, 0, buffer.Length);
                stream.Seek(0, SeekOrigin.Begin);

                var secureServerSend = tcpServer.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                var secureClientReceive = tcpClient.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                tcpServer.EndSend(secureServerSend);
                var returnStream = tcpClient.EndReceive(secureClientReceive);

                var buff2 = new byte[(int)returnStream.Length];
                returnStream.Read(buff2, 0, buff2.Length);

                Assert.IsTrue(Collection.Equals(buffer, buff2), "TcpConnection #2");
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_CrcConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            var crcClient = new CrcConnection(new TcpConnection(client.Client, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), _bufferManager);
            var crcServer = new CrcConnection(new TcpConnection(server, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), _bufferManager);

            using (MemoryStream stream = new MemoryStream())
            {
                var buffer = new byte[1024 * 8];
                _random.NextBytes(buffer);

                stream.Write(buffer, 0, buffer.Length);
                stream.Seek(0, SeekOrigin.Begin);

                var secureClientSend = crcClient.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                var secureServerReceive = crcServer.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                crcClient.EndSend(secureClientSend);
                var returnStream = crcServer.EndReceive(secureServerReceive);

                var buff2 = new byte[(int)returnStream.Length];
                returnStream.Read(buff2, 0, buff2.Length);

                Assert.IsTrue(Collection.Equals(buffer, buff2), "CrcConnection #1");
            }

            using (MemoryStream stream = new MemoryStream())
            {
                var buffer = new byte[1024 * 8];
                _random.NextBytes(buffer);

                stream.Write(buffer, 0, buffer.Length);
                stream.Seek(0, SeekOrigin.Begin);

                var secureServerSend = crcServer.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                var secureClientReceive = crcClient.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                crcServer.EndSend(secureServerSend);
                var returnStream = crcClient.EndReceive(secureClientReceive);

                var buff2 = new byte[(int)returnStream.Length];
                returnStream.Read(buff2, 0, buff2.Length);

                Assert.IsTrue(Collection.Equals(buffer, buff2), "CrcConnection #2");
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_CompressConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            using (var compressClient = new CompressConnection(new TcpConnection(client.Client, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            using (var compressServer = new CompressConnection(new TcpConnection(server, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            {
                var compressClientConnect = compressClient.BeginConnect(new TimeSpan(0, 0, 20), null, null);
                var compressServerConnect = compressServer.BeginConnect(new TimeSpan(0, 0, 20), null, null);

                compressClient.EndClose(compressClientConnect);
                compressServer.EndClose(compressServerConnect);

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    //_random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var secureClientSend = compressClient.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                    var secureServerReceive = compressServer.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                    compressClient.EndSend(secureClientSend);
                    var returnStream = compressServer.EndReceive(secureServerReceive);

                    var buff2 = new byte[(int)returnStream.Length];
                    returnStream.Read(buff2, 0, buff2.Length);

                    Assert.IsTrue(Collection.Equals(buffer, buff2), "CompressConnection #1");
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    //_random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var secureServerSend = compressServer.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                    var secureClientReceive = compressClient.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                    compressServer.EndSend(secureServerSend);
                    var returnStream = compressClient.EndReceive(secureClientReceive);

                    var buff2 = new byte[(int)returnStream.Length];
                    returnStream.Read(buff2, 0, buff2.Length);

                    Assert.IsTrue(Collection.Equals(buffer, buff2), "CompressConnection #2");
                }
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_SecureConnection()
        {
            for (int i = 0; i < 4; i++)
            {
                TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
                listener.Start();
                var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

                TcpClient client = new TcpClient();
                client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

                var server = listener.EndAcceptSocket(listenerAcceptSocket);
                listener.Stop();

                DigitalSignature clientDigitalSignature = null;
                DigitalSignature serverDigitalSignature = null;

                if (2 <= i)
                {
                    clientDigitalSignature = new DigitalSignature("NickName1", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
                    serverDigitalSignature = new DigitalSignature("NickName2", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
                }

                SecureConnectionVersion clientVersion = SecureConnectionVersion.Version1;
                SecureConnectionVersion serverVersion = SecureConnectionVersion.Version1 | SecureConnectionVersion.Version2;

                if (i == 0 || i == 1) clientVersion = SecureConnectionVersion.Version1;
                if (i == 2 || i == 3) clientVersion = SecureConnectionVersion.Version2;

                ////var TcpClient = new TcpConnection(client.Client, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager);
                using (var secureClient = new SecureConnection(SecureConnectionType.Client, clientVersion, new TcpConnection(client.Client, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), clientDigitalSignature, _bufferManager))
                using (var secureServer = new SecureConnection(SecureConnectionType.Server, serverVersion, new TcpConnection(server, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), serverDigitalSignature, _bufferManager))
                {
                    var secureClientConnect = secureClient.BeginConnect(new TimeSpan(0, 0, 20), null, null);
                    var secureServerConnect = secureServer.BeginConnect(new TimeSpan(0, 0, 20), null, null);

                    secureClient.EndClose(secureClientConnect);
                    secureServer.EndClose(secureServerConnect);

                    if (2 <= i)
                    {
                        if (secureClient.Certificate.ToString() != serverDigitalSignature.ToString()) throw new Exception();
                        if (secureServer.Certificate.ToString() != clientDigitalSignature.ToString()) throw new Exception();
                    }

                    using (MemoryStream stream = new MemoryStream())
                    {
                        var buffer = new byte[1024 * 8];
                        _random.NextBytes(buffer);

                        stream.Write(buffer, 0, buffer.Length);
                        stream.Seek(0, SeekOrigin.Begin);

                        var secureClientSend = secureClient.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                        var secureServerReceive = secureServer.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                        secureClient.EndSend(secureClientSend);
                        var returnStream = secureServer.EndReceive(secureServerReceive);

                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(Collection.Equals(buffer, buff2), "SecureConnection #1");
                    }

                    using (MemoryStream stream = new MemoryStream())
                    {
                        var buffer = new byte[1024 * 8];
                        _random.NextBytes(buffer);

                        stream.Write(buffer, 0, buffer.Length);
                        stream.Seek(0, SeekOrigin.Begin);

                        var secureServerSend = secureServer.BeginSend(stream, new TimeSpan(0, 0, 20), null, null);
                        var secureClientReceive = secureClient.BeginReceive(new TimeSpan(0, 0, 20), null, null);

                        secureServer.EndSend(secureServerSend);
                        var returnStream = secureClient.EndReceive(secureClientReceive);

                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(Collection.Equals(buffer, buff2), "SecureConnection #2");
                    }
                }

                client.Close();
                server.Close();
            }
        }
    }
}
