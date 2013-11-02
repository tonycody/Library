using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Library.Net.Connection;
using Library.Security;
using NUnit.Framework;
using System.Threading.Tasks;

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

                var clientSendTask = tcpClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                var serverReceiveTask = tcpServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(clientSendTask, serverReceiveTask);

                var returnStream = serverReceiveTask.Result;

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

                var serverSendTask = tcpServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                var clientReceiveTask = tcpClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(serverSendTask, clientReceiveTask);

                var returnStream = clientReceiveTask.Result;

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

                var clientSendTask = crcClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                var serverReceiveTask = crcServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(clientSendTask, serverReceiveTask);

                var returnStream = serverReceiveTask.Result;

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

                var serverSendTask = crcServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                var clientReceiveTask = crcClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(serverSendTask, clientReceiveTask);

                var returnStream = clientReceiveTask.Result;

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
                var clientConnectTask = compressClient.ConnectAsync(new TimeSpan(0, 0, 20));
                var serverConnectTask = compressServer.ConnectAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(clientConnectTask, serverConnectTask);

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var clientSendTask = compressClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var serverReceiveTask = compressServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(clientConnectTask, serverReceiveTask);

                    var returnStream = serverReceiveTask.Result;

                    var buff2 = new byte[(int)returnStream.Length];
                    returnStream.Read(buff2, 0, buff2.Length);

                    Assert.IsTrue(Collection.Equals(buffer, buff2), "CompressConnection #1");
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var serverSendTask = compressServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var clientReceiveTask = compressClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(serverSendTask, clientReceiveTask);

                    var returnStream = clientReceiveTask.Result;

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
            for (int i = 0; i < 32; i++)
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

                if (_random.Next(0, 100) < 50)
                {
                    clientDigitalSignature = new DigitalSignature("NickName1", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
                }

                if (_random.Next(0, 100) < 50)
                {
                    serverDigitalSignature = new DigitalSignature("NickName2", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
                }

                SecureConnectionVersion clientVersion = 0;
                SecureConnectionVersion serverVersion = 0;

                for (; ; )
                {
                    switch (_random.Next(0, 3))
                    {
                        case 0:
                            clientVersion = SecureConnectionVersion.Version1;
                            break;
                        case 1:
                            clientVersion = SecureConnectionVersion.Version2;
                            break;
                        case 2:
                            clientVersion = SecureConnectionVersion.Version1 | SecureConnectionVersion.Version2;
                            break;
                    }

                    switch (_random.Next(0, 3))
                    {
                        case 0:
                            serverVersion = SecureConnectionVersion.Version1;
                            break;
                        case 1:
                            serverVersion = SecureConnectionVersion.Version2;
                            break;
                        case 2:
                            serverVersion = SecureConnectionVersion.Version1 | SecureConnectionVersion.Version2;
                            break;
                    }

                    if ((clientVersion & serverVersion) != 0) break;
                }

                ////var TcpClient = new TcpConnection(client.Client, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager);
                using (var secureClient = new SecureConnection(SecureConnectionType.Client, clientVersion, new TcpConnection(client.Client, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), clientDigitalSignature, _bufferManager))
                using (var secureServer = new SecureConnection(SecureConnectionType.Server, serverVersion, new TcpConnection(server, null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), serverDigitalSignature, _bufferManager))
                {
                    try
                    {
                        var clientConnectTask = secureClient.ConnectAsync(new TimeSpan(0, 0, 20));
                        var serverConnectTask = secureServer.ConnectAsync(new TimeSpan(0, 0, 20));

                        Task.WaitAll(clientConnectTask, serverConnectTask);

                        if (clientDigitalSignature != null)
                        {
                            if (secureServer.Certificate.ToString() != clientDigitalSignature.ToString()) throw new Exception();
                        }

                        if (serverDigitalSignature != null)
                        {
                            if (secureClient.Certificate.ToString() != serverDigitalSignature.ToString()) throw new Exception();
                        }

                        using (MemoryStream stream = new MemoryStream())
                        {
                            var buffer = new byte[1024 * 8];
                            _random.NextBytes(buffer);

                            stream.Write(buffer, 0, buffer.Length);
                            stream.Seek(0, SeekOrigin.Begin);

                            var clientSendTask = secureClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                            var serverReceiveTask = secureServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                            Task.WaitAll(clientConnectTask, serverReceiveTask);

                            var returnStream = serverReceiveTask.Result;

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

                            var serverSendTask = secureServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                            var clientReceiveTask = secureClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                            Task.WaitAll(serverSendTask, clientReceiveTask);

                            var returnStream = clientReceiveTask.Result;

                            var buff2 = new byte[(int)returnStream.Length];
                            returnStream.Read(buff2, 0, buff2.Length);

                            Assert.IsTrue(Collection.Equals(buffer, buff2), "SecureConnection #2");
                        }
                    }
                    catch (AggregateException e)
                    {
                        Assert.IsTrue(e.InnerException.GetType() == typeof(ConnectionException)
                            && (clientVersion & serverVersion) == 0, "SecureConnection #Version test");
                    }
                }

                client.Close();
                server.Close();
            }
        }
    }
}
