using System;
using System.Collections.Generic;
using Library.Net.Lair;
using Library.Security;
using NUnit.Framework;
using System.Text;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Lair")]
    public class Test_Library_Net_Lair
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_LairConverter_Node()
        {
            var node = new Node();
            var id = new byte[64];
            _random.NextBytes(id);
            node.Id = id;
            node.Uris.AddRange(new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" });

            var stringNode = LairConverter.ToNodeString(node);
            var node2 = LairConverter.FromNodeString(stringNode);

            Assert.AreEqual(node, node2, "LairConverter #1");
        }

        [Test]
        public void Test_LairConverter_Section()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            _random.NextBytes(id);

            var section = new Section(id, "aoeui");

            var stream = LairConverter.ToSectionString(section, digitalSignature.ToString());

            string sectionSignature;
            var value = LairConverter.FromSectionString(stream, out sectionSignature);

            Assert.AreEqual(section, value, "LairConverter #2");
            Assert.AreEqual(digitalSignature.ToString(), sectionSignature, "LairConverter #3");
        }

        [Test]
        public void Test_LairConverter_Channel()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            _random.NextBytes(id);

            var channel = new Channel(id, "aoeui");

            var stream = LairConverter.ToChannelString(channel);
            var value = LairConverter.FromChannelString(stream);

            Assert.AreEqual(channel, value, "LairConverter #4");
        }

        [Test]
        public void Test_ContentConverter_ProfileContent()
        {
            Exchange exchange = new Exchange(ExchangeAlgorithm.Rsa2048);
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);

            var signatures = new List<string>();
            signatures.Add(digitalSignature.ToString());

            var channels = new List<Channel>();
            channels.Add(new Channel(new byte[64], "123"));

            ProfileContent content = new ProfileContent(signatures, channels, exchange.ExchangeAlgorithm, exchange.PublicKey);
            var binaryContent = ContentConverter.ToProfileContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromProfileContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_DocumentContent()
        {
            var pages = new List<Page>();

            //for (int i = 0; i < DocumentContent.MaxPagesCount; i++)
            for (int i = 0; i < 32; i++)
            {
                StringBuilder sb = new StringBuilder();

                for (int j = 0; j < Page.MaxTextLength; j++)
                {
                    sb.Append("0");
                }

                pages.Add(new Page("123", ContentFormatType.MiniWiki, sb.ToString()));
            }

            DocumentContent content = new DocumentContent(pages);
            var binaryContent = ContentConverter.ToDocumentContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromDocumentContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_TopicContent()
        {
            TopicContent content = new TopicContent("123", ContentFormatType.MiniWiki);
            var binaryContent = ContentConverter.ToTopicContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromTopicContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_MessageContent()
        {
            var keys = new List<Key>();
            keys.Add(new Key(new byte[64], HashAlgorithm.Sha512));

            MessageContent content = new MessageContent("123", keys);
            var binaryContent = ContentConverter.ToMessageContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromMessageContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_MailContent()
        {
            Exchange exchange = new Exchange(ExchangeAlgorithm.Rsa2048);
            MailContent content = new MailContent("test");
            var binaryContent = ContentConverter.ToMailContentBlock(content, exchange);

            Assert.AreEqual(content, ContentConverter.FromMailContentBlock(binaryContent, exchange));
        }

        [Test]
        public void Test_Node()
        {
            var node = new Node();
            var id = new byte[64];
            _random.NextBytes(id);
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
    }
}
