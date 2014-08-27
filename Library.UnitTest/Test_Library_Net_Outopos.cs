using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Library.Collections;
using Library.Net;
using Library.Net.Outopos;
using Library.Net.Connections;
using Library.Security;
using NUnit.Framework;
using System.Runtime.Serialization;
using Library.Io;
using System.Xml;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Outopos")]
    public class Test_Library_Net_Outopos
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        private const int MaxReceiveCount = 32 * 1024 * 1024;

        [Test]
        public void Test_OutoposConverter_Node()
        {
            Node node = null;

            {
                var id = new byte[64];
                _random.NextBytes(id);
                var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                node = new Node(id, uris);
            }

            var stringNode = OutoposConverter.ToNodeString(node);
            var node2 = OutoposConverter.FromNodeString(stringNode);

            Assert.AreEqual(node, node2, "OutoposConverter #1");
        }

        [Test]
        public void Test_OutoposConverter_Wiki()
        {
            Wiki tag1 = new Wiki("oooo", new byte[64]);
            Wiki tag2;

            var stringTagAndOption = OutoposConverter.ToWikiString(tag1);
            tag2 = OutoposConverter.FromWikiString(stringTagAndOption);

            Assert.AreEqual(tag1, tag2, "OutoposConverter #2");
        }

        [Test]
        public void Test_OutoposConverter_Chat()
        {
            Chat tag1 = new Chat("oooo", new byte[64]);
            Chat tag2;

            var stringTagAndOption = OutoposConverter.ToChatString(tag1);
            tag2 = OutoposConverter.FromChatString(stringTagAndOption);

            Assert.AreEqual(tag1, tag2, "OutoposConverter #3ll");
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

            Node node2;

            using (var nodeStream = node.Export(_bufferManager))
            {
                node2 = Node.Import(nodeStream, _bufferManager);
            }

            Assert.AreEqual(node, node2, "Node #1");
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

            Key key2;

            using (var keyStream = key.Export(_bufferManager))
            {
                key2 = Key.Import(keyStream, _bufferManager);
            }

            Assert.AreEqual(key, key2, "Key #1");
        }

        [Test]
        public void Test_Tag()
        {
            var tag = new Chat("oooo", new byte[64]);

            Chat tag2;
            {
                var ds = new DataContractSerializer(typeof(Chat));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (WrapperStream wrapperStream = new WrapperStream(stream, true))
                    using (XmlDictionaryWriter xmlDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(wrapperStream))
                    {
                        ds.WriteObject(xmlDictionaryWriter, tag);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader xmlDictionaryReader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        tag2 = (Chat)ds.ReadObject(xmlDictionaryReader);
                    }
                }
            }

            Assert.AreEqual(tag, tag2, "Tag #1");

            Chat tag3;

            using (var tagStream = tag.Export(_bufferManager))
            {
                tag3 = Chat.Import(tagStream, _bufferManager);
            }

            Assert.AreEqual(tag, tag3, "Tag #2");
        }

        [Test]
        public void Test_Metadata()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.EcDsaP521_Sha512 })
            {
                var id = new byte[64];
                _random.NextBytes(id);
                var key = new Key(id, HashAlgorithm.Sha512);
                var tag = new Chat("oooo", new byte[64]);
                var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                var digitalSignature = new DigitalSignature("123", a);
                var metadata = new ChatMessageMetadata(tag, DateTime.UtcNow, key, miner, digitalSignature);

                ChatMessageMetadata metadata2;
                {
                    var ds = new DataContractSerializer(typeof(ChatMessageMetadata));

                    using (BufferStream stream = new BufferStream(BufferManager.Instance))
                    {
                        using (WrapperStream wrapperStream = new WrapperStream(stream, true))
                        using (XmlDictionaryWriter xmlDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(wrapperStream))
                        {
                            ds.WriteObject(xmlDictionaryWriter, metadata);
                        }

                        stream.Position = 0;

                        using (XmlDictionaryReader xmlDictionaryReader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
                        {
                            metadata2 = (ChatMessageMetadata)ds.ReadObject(xmlDictionaryReader);
                        }
                    }
                }

                Assert.AreEqual(metadata, metadata2, "Metadata #1");

                ChatMessageMetadata metadata3;

                using (var metadataStream = metadata.Export(_bufferManager))
                {
                    metadata3 = ChatMessageMetadata.Import(metadataStream, _bufferManager);
                }

                Assert.AreEqual(metadata, metadata3, "Metadata #2");
                Assert.IsTrue(metadata3.VerifyCertificate(), "Metadata #3");
            }
        }

        [Test]
        public void Test_BitmapManager()
        {
            using (BitmapManager bitmapManager = new BitmapManager("bitmap", _bufferManager))
            {
                bitmapManager.SetLength(1024 * 256);

                Random random_a, random_b;

                {
                    var seed = _random.Next();

                    random_a = new Random(seed);
                    random_b = new Random(seed);
                }

                {
                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        bitmapManager.Set(p, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(bitmapManager.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (bitmapManager.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }

                {
                    for (int i = 0; i < 1024 * 256; i++)
                    {
                        bitmapManager.Set(i, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        bitmapManager.Set(p, false);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(!bitmapManager.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (!bitmapManager.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }
            }
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

                var tcpClient = new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Outopos.MaxReceiveCount, _bufferManager);
                var tcpServer = new BaseConnection(new SocketCap(server), null, Test_Library_Net_Outopos.MaxReceiveCount, _bufferManager);

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

                    serverConnectionManager = new ConnectionManager(tcpServer, serverSessionId, serverNode, ConnectDirection.In, _bufferManager);
                    clientConnectionManager = new ConnectionManager(tcpClient, clientSessionId, clientNode, ConnectDirection.Out, _bufferManager);

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

                    Assert.IsTrue(CollectionUtilities.Equals(serverConnectionManager.SesstionId, clientSessionId), "ConnectionManager SessionId #1");
                    Assert.IsTrue(CollectionUtilities.Equals(clientConnectionManager.SesstionId, serverSessionId), "ConnectionManager SessionId #2");

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
                    Assert.IsTrue(CollectionUtilities.Equals(nodes, item.Nodes), "ConnectionManager #1");
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
                    Assert.IsTrue(CollectionUtilities.Equals(keys, item.Keys), "ConnectionManager #2");
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
                    Assert.IsTrue(CollectionUtilities.Equals(keys, item.Keys), "ConnectionManager #3");
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
                    Assert.AreEqual(key, item.Key, "ConnectionManager #4.1");
                    Assert.IsTrue(CollectionUtilities.Equals(buffer, 0, item.Value.Array, item.Value.Offset, 1024 * 1024 * 4), "ConnectionManager #4.2");

                    _bufferManager.ReturnBuffer(buffer);
                    _bufferManager.ReturnBuffer(item.Value.Array);
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBroadcastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBroadcastMetadatasRequestEvent += (object sender, PullBroadcastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.EcDsaP521_Sha512);

                    var signatures = new SignatureCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        signatures.Add(digitalSignature.ToString());
                    }

                    senderConnection.PushBroadcastMetadatasRequest(signatures);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(signatures, item.Signatures), "ConnectionManager #5.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBroadcastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBroadcastMetadatasEvent += (object sender, PullBroadcastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.EcDsaP521_Sha512);

                    var metadatas1 = new List<ProfileMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var metadata = new ProfileMetadata(DateTime.UtcNow, key, miner, digitalSignature);

                        metadatas1.Add(metadata);
                    }

                    senderConnection.PushBroadcastMetadatas(metadatas1);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(metadatas1, item.ProfileMetadatas), "ConnectionManager #6.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullUnicastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullUnicastMetadatasRequestEvent += (object sender, PullUnicastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.EcDsaP521_Sha512);

                    var signatures = new SignatureCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        signatures.Add(digitalSignature.ToString());
                    }

                    senderConnection.PushUnicastMetadatasRequest(signatures);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(signatures, item.Signatures), "ConnectionManager #7.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullUnicastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullUnicastMetadatasEvent += (object sender, PullUnicastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.EcDsaP521_Sha512);

                    var metadatas1 = new List<SignatureMessageMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var metadata = new SignatureMessageMetadata(digitalSignature.ToString(), DateTime.UtcNow, key, miner, digitalSignature);

                        metadatas1.Add(metadata);
                    }

                    senderConnection.PushUnicastMetadatas(metadatas1);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(metadatas1, item.SignatureMessageMetadatas), "ConnectionManager #8.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullMulticastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullMulticastMetadatasRequestEvent += (object sender, PullMulticastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var wikis = new WikiCollection();
                    var chats = new ChatCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);

                        wikis.Add(new Wiki(RandomString.GetValue(256), id));
                    }

                    for (int j = 0; j < 32; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);

                        chats.Add(new Chat(RandomString.GetValue(256), id));
                    }

                    senderConnection.PushMulticastMetadatasRequest(wikis, chats);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(wikis, item.Wikis), "ConnectionManager #9.1");
                    Assert.IsTrue(CollectionUtilities.Equals(chats, item.Chats), "ConnectionManager #9.2");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullMulticastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullMulticastMetadatasEvent += (object sender, PullMulticastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.EcDsaP521_Sha512);

                    var metadatas1 = new List<WikiDocumentMetadata>();
                    var metadatas2 = new List<ChatTopicMetadata>();
                    var metadatas3 = new List<ChatMessageMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);
                        var tag = new Wiki("oooo", new byte[64]);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var metadata = new WikiDocumentMetadata(tag, DateTime.UtcNow, key, miner, digitalSignature);

                        metadatas1.Add(metadata);
                    }

                    for (int j = 0; j < 4; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);
                        var tag = new Chat("oooo", new byte[64]);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var metadata = new ChatTopicMetadata(tag, DateTime.UtcNow, key, miner, digitalSignature);

                        metadatas2.Add(metadata);
                    }

                    for (int j = 0; j < 4; j++)
                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var key = new Key(id, HashAlgorithm.Sha512);
                        var tag = new Chat("oooo", new byte[64]);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var metadata = new ChatMessageMetadata(tag, DateTime.UtcNow, key, miner, digitalSignature);

                        metadatas3.Add(metadata);
                    }

                    senderConnection.PushMulticastMetadatas(metadatas1, metadatas2, metadatas3);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtilities.Equals(metadatas1, item.WikiDocumentMetadatas), "ConnectionManager #10.1");
                    Assert.IsTrue(CollectionUtilities.Equals(metadatas2, item.ChatTopicMetadatas), "ConnectionManager #10.2");
                    Assert.IsTrue(CollectionUtilities.Equals(metadatas3, item.ChatMessageMetadatas), "ConnectionManager #10.3");
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
