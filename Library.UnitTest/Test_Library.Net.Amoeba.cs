using System;
using Library.Net.Amoeba;
using Library.Security;
using NUnit.Framework;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using Library.Net.Connections;
using Library.Collections;
using System.Threading;
using Library.Net.Caps;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Amoeba")]
    public class Test_Library_Net_Amoeba
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        private const int MaxReceiveCount = 32 * 1024 * 1024;

        [Test]
        public void Test_AmoebaConverter_Node()
        {
            Node node = null;

            {
                var id = new byte[64];
                _random.NextBytes(id);
                var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                node = new Node(id, uris);
            }

            var stringNode = AmoebaConverter.ToNodeString(node);
            var node2 = AmoebaConverter.FromNodeString(stringNode);

            Assert.AreEqual(node, node2, "AmoebaConverter #1");
        }

        [Test]
        public void Test_AmoebaConverter_Seed()
        {
            var seed = new Seed();
            seed.Name = "aaaa.zip";
            seed.Keywords.AddRange(new KeywordCollection 
            {
                "bbbb",
                "cccc",
                "dddd",
            });
            seed.CreationTime = DateTime.Now;
            seed.Length = 10000;
            seed.Comment = "eeee";
            seed.Rank = 1;
            seed.Key = new Key(new byte[64], HashAlgorithm.Sha512);
            seed.CompressionAlgorithm = CompressionAlgorithm.Lzma;
            seed.CryptoAlgorithm = CryptoAlgorithm.Rijndael256;
            seed.CryptoKey = new byte[32 + 32];

            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha512);
            seed.CreateCertificate(digitalSignature);

            var stringSeed = AmoebaConverter.ToSeedString(seed);
            var seed2 = AmoebaConverter.FromSeedString(stringSeed);

            Assert.AreEqual(seed, seed2, "AmoebaConverter #2");
        }

        [Test]
        public void Test_AmoebaConverter_Box()
        {
            var box = new Box();
            box.Name = "Box";
            box.Comment = "Comment";
            box.CreationTime = DateTime.Now;
            box.Boxes.Add(new Box() { Name = "Box" });
            box.Seeds.Add(new Seed() { Name = "Seed" });

            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            box.CreateCertificate(digitalSignature);

            var streamBox = AmoebaConverter.ToBoxStream(box);
            var box2 = AmoebaConverter.FromBoxStream(streamBox);

            Assert.AreEqual(box, box2, "AmoebaConverter #3");
        }

        [Test]
        public void Test_Node()
        {
            Node node = null;

            {
                var id = new byte[64];
                _random.NextBytes(id);
                var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                node = new Node(id, uris);
            }

            var node2 = node.DeepClone();

            Assert.AreEqual(node, node2, "Node #1");

            Node node3;

            using (var nodeStream = node.Export(_bufferManager))
            {
                node3 = Node.Import(nodeStream, _bufferManager);
            }

            Assert.AreEqual(node, node3, "Node #2");
        }

        [Test]
        public void Test_Key()
        {
            Key key = null;

            {
                var id = new byte[64];
                _random.NextBytes(id);

                key = new Key(id, HashAlgorithm.Sha512);
            }

            var key2 = key.DeepClone();

            Assert.AreEqual(key, key2, "Key #1");

            Key key3;

            using (var keyStream = key.Export(_bufferManager))
            {
                key3 = Key.Import(keyStream, _bufferManager);
            }

            Assert.AreEqual(key, key3, "Key #2");
        }

        [Test]
        public void Test_Seed()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.ECDsaP521_Sha512 })
            {
                var seed = new Seed();
                seed.Name = "aaaa.zip";
                seed.Keywords.AddRange(new KeywordCollection 
                {
                    "bbbb",
                    "cccc",
                    "dddd",
                });
                seed.CreationTime = DateTime.Now;
                seed.Length = 10000;
                seed.Comment = "eeee";
                seed.Rank = 1;
                seed.Key = new Key(new byte[64], HashAlgorithm.Sha512);
                seed.CompressionAlgorithm = CompressionAlgorithm.Lzma;
                seed.CryptoAlgorithm = CryptoAlgorithm.Rijndael256;
                seed.CryptoKey = new byte[32 + 32];

                DigitalSignature digitalSignature = new DigitalSignature("123", a);
                seed.CreateCertificate(digitalSignature);

                var seed2 = seed.DeepClone();

                Assert.AreEqual(seed, seed2, "Seed #1");

                Seed seed3;

                using (var seedStream = seed.Export(_bufferManager))
                {
                    var buffer = new byte[seedStream.Length];
                    seedStream.Read(buffer, 0, buffer.Length);

                    seedStream.Position = 0;
                    seed3 = Seed.Import(seedStream, _bufferManager);
                }

                Assert.AreEqual(seed, seed3, "Seed #2");
                Assert.IsTrue(seed3.VerifyCertificate(), "Seed #3");
            }
        }

        [Test]
        public void Test_Box()
        {
            var box = new Box();
            box.Name = "Box";
            box.Comment = "Comment";
            box.CreationTime = DateTime.Now;
            box.Seeds.Add(new Seed() { Name = "Seed" });
            box.Boxes.Add(new Box() { Name = "Box" });

            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            box.CreateCertificate(digitalSignature);

            var box2 = box.DeepClone();

            Assert.AreEqual(box, box2, "Box #1");

            Box box3;

            using (var boxStream = box.Export(_bufferManager))
            {
                var buffer = new byte[boxStream.Length];
                boxStream.Read(buffer, 0, buffer.Length);

                boxStream.Position = 0;
                box3 = Box.Import(boxStream, _bufferManager);
            }

            Assert.AreEqual(box, box3, "Box #2");
            Assert.IsTrue(box3.VerifyCertificate(), "Box #3");
        }

        [Test]
        public void Test_Store()
        {
            var store = new Store();
            store.Boxes.Add(new Box() { Name = "Box" });

            var store2 = store.DeepClone();

            Assert.AreEqual(store, store2, "Store #1");

            Store store3;

            using (var storeStream = store.Export(_bufferManager))
            {
                var buffer = new byte[storeStream.Length];
                storeStream.Read(buffer, 0, buffer.Length);

                storeStream.Position = 0;
                store3 = Store.Import(storeStream, _bufferManager);
            }

            Assert.AreEqual(store, store3, "Store #2");
        }

        [Test]
        public void Test_ConnectionManager()
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

                var tcpClient = new CapConnection(new SocketCap(client.Client), null, Test_Library_Net_Amoeba.MaxReceiveCount, _bufferManager);
                var tcpServer = new CapConnection(new SocketCap(server), null, Test_Library_Net_Amoeba.MaxReceiveCount, _bufferManager);

                List<ConnectionManager> connectionManagers = new List<ConnectionManager>();

                {
                    ConnectionManager serverConnectionManager;
                    ConnectionManager clientConnectionManager;

                    Node serverNode = null;
                    Node clientNode = null;

                    byte[] serverSessionId = null;
                    byte[] clientSessionId = null;

                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                        serverNode = new Node(id, uris);
                    }

                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                        clientNode = new Node(id, uris);
                    }

                    {
                        serverSessionId = new byte[64];
                        _random.NextBytes(serverSessionId);
                    }

                    {
                        clientSessionId = new byte[64];
                        _random.NextBytes(clientSessionId);
                    }

                    serverConnectionManager = new ConnectionManager(tcpServer, serverSessionId, serverNode, ConnectionManagerType.Server, _bufferManager);
                    clientConnectionManager = new ConnectionManager(tcpClient, clientSessionId, clientNode, ConnectionManagerType.Client, _bufferManager);

                    Thread serverThread = new Thread(new ThreadStart(() =>
                    {
                        serverConnectionManager.Connect();
                    }));

                    Thread clientThread = new Thread(new ThreadStart(() =>
                    {
                        clientConnectionManager.Connect();
                    }));

                    serverThread.Start();
                    clientThread.Start();

                    serverThread.Join();
                    clientThread.Join();

                    Assert.IsTrue(Collection.Equals(serverConnectionManager.SesstionId, clientSessionId), "ConnectionManager SessionId #1");
                    Assert.IsTrue(Collection.Equals(clientConnectionManager.SesstionId, serverSessionId), "ConnectionManager SessionId #2");

                    Assert.AreEqual(serverConnectionManager.Node, clientNode, "ConnectionManager Node #1");
                    Assert.AreEqual(clientConnectionManager.Node, serverNode, "ConnectionManager Node #2");

                    connectionManagers.Add(serverConnectionManager);
                    connectionManagers.Add(clientConnectionManager);
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullNodesEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullNodesEvent += (object sender, PullNodesEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    List<Node> nodes = new List<Node>();

                    for (int j = 0; j < 32; j++)
                    {
                        Node node = null;

                        {
                            var id = new byte[64];
                            _random.NextBytes(id);
                            var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                            node = new Node(id, uris);
                        }

                        nodes.Add(node);
                    }

                    senderConnection.PushNodes(nodes);

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(nodes, item.Nodes), "ConnectionManager #1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlocksLinkEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlocksLinkEvent += (object sender, PullBlocksLinkEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var keys = new List<Key>();

                    for (int j = 0; j < 32; j++)
                    {
                        Key key = null;

                        {
                            var id = new byte[64];
                            _random.NextBytes(id);

                            key = new Key(id, HashAlgorithm.Sha512);
                        }

                        keys.Add(key);
                    }

                    senderConnection.PushBlocksLink(keys);

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(keys, item.Keys), "ConnectionManager #2");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlocksRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlocksRequestEvent += (object sender, PullBlocksRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var keys = new List<Key>();

                    for (int j = 0; j < 32; j++)
                    {
                        Key key = null;

                        {
                            var id = new byte[64];
                            _random.NextBytes(id);

                            key = new Key(id, HashAlgorithm.Sha512);
                        }

                        keys.Add(key);
                    }

                    senderConnection.PushBlocksRequest(keys);

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(keys, item.Keys), "ConnectionManager #3");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlockEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlockEvent += (object sender, PullBlockEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var buffer = _bufferManager.TakeBuffer(1024 * 1024 * 8);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    senderConnection.PushBlock(key, new ArraySegment<byte>(buffer, 0, 1024 * 1024 * 4));

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(key, item.Key), "ConnectionManager #4");
                    Assert.IsTrue(Collection.Equals(buffer, 0, item.Value.Array, item.Value.Offset, 1024 * 1024 * 4), "ConnectionManager #5");

                    _bufferManager.ReturnBuffer(buffer);
                    _bufferManager.ReturnBuffer(item.Value.Array);
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullSeedsRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullSeedsRequestEvent += (object sender, PullSeedsRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var signatures = new SignatureCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        signatures.Add(RandomString.GetValue(256));
                    }

                    senderConnection.PushSeedsRequest(signatures);

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(signatures, item.Signatures), "ConnectionManager #6");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullSeedsEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullSeedsEvent += (object sender, PullSeedsEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    List<Seed> seeds = new List<Seed>();

                    for (int j = 0; j < 32; j++)
                    {
                        var seed = new Seed();
                        seed.Name = "aaaa.zip";
                        seed.Keywords.AddRange(new KeywordCollection 
                        {
                            "bbbb",
                            "cccc",
                            "dddd",
                        });
                        seed.CreationTime = DateTime.Now;
                        seed.Length = 10000;
                        seed.Comment = "eeee";
                        seed.Rank = 1;
                        seed.Key = new Key(new byte[64], HashAlgorithm.Sha512);
                        seed.CompressionAlgorithm = CompressionAlgorithm.Lzma;
                        seed.CryptoAlgorithm = CryptoAlgorithm.Rijndael256;
                        seed.CryptoKey = new byte[32 + 32];

                        seeds.Add(seed);
                    }

                    senderConnection.PushSeeds(seeds);

                    var item = queue.Dequeue();
                    Assert.IsTrue(Collection.Equals(seeds, item.Seeds), "ConnectionManager #7");
                }

                foreach (var connectionManager in connectionManagers)
                {
                    connectionManager.Dispose();
                }

                client.Close();
                server.Close();
            }
        }
    }
}
