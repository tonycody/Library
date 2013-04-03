using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Library.Collections;
using Library.Net;
using Library.Net.Lair;
using Library.Security;
using NUnit.Framework;
using System.Threading.Tasks;
using Library.Io;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Lair")]
    public class Test_Library_Net_Lair
    {
        private BufferManager _bufferManager = new BufferManager();

        [TearDown]
        public void TearDown()
        {
            _bufferManager.Dispose();
        }

        [Test]
        public void Test_LairConverter_Node()
        {
            var node = new Node();
            var id = new byte[64];
            new Random().NextBytes(id);
            node.Id = id;
            node.Uris.AddRange(new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" });

            var stringNode = LairConverter.ToNodeString(node);
            var node2 = LairConverter.FromNodeString(stringNode);

            Assert.AreEqual(node, node2, "LairConverter #1");
        }

        [Test]
        public void Test_LairConverter_Channel()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            new Random().NextBytes(id);

            var channel = new Channel(id, "aoeui");

            var stream = LairConverter.ToChannelString(channel);
            var value = LairConverter.FromChannelString(stream);

            Assert.AreEqual(channel, value, "LairConverter #2");
        }

        [Test]
        public void Test_LairConverter_Message()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            new Random().NextBytes(id);

            var message = new Message(new Channel(id, "aoeui"), "aoeui", null, digitalSignature);

            var stream = LairConverter.ToMessageString(message);
            var value = LairConverter.FromMessageString(stream);

            Assert.AreEqual(message, value, "LairConverter #3");
        }

        [Test]
        public void Test_Node()
        {
            var node = new Node();
            var id = new byte[64];
            new Random().NextBytes(id);
            node.Id = id;
            node.Uris.AddRange(new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" });

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
            var key = new Key(new byte[64], HashAlgorithm.Sha512);
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
        public void Test_Message()
        {
            Parallel.For(0, 1024, new ParallelOptions() { MaxDegreeOfParallelism = 64 }, i =>
            {
                ////using (MemoryStream stream = new MemoryStream())
                using (BufferStream bufferStream = new BufferStream(_bufferManager))
                using (CacheStream stream = new CacheStream(bufferStream, 1024, _bufferManager))
                {
                    var id = new byte[64];
                    var channelNameBuffer = new byte[256];
                    var contentBuffer = new byte[1024 * 2];

                    new Random().NextBytes(id);
                    new Random().NextBytes(channelNameBuffer);
                    new Random().NextBytes(contentBuffer);

                    var channel = new Channel(id, NetworkConverter.ToBase64UrlString(channelNameBuffer).Substring(0, 256));
                    var message = new Message(channel, NetworkConverter.ToBase64UrlString(contentBuffer).Substring(0, 1024 * 2), null, null);

                    var buffer = new byte[1024];

                    using (var inStream = message.Export(_bufferManager))
                    {
                        int count = 0;

                        while ((count = inStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            stream.Write(buffer, 0, count);
                        }
                    }

                    stream.Position = 0;

                    var message2 = Message.Import(stream, _bufferManager);

                    Assert.AreEqual(message, message2, "Message #1");
                }
            });
        }
    }
}
