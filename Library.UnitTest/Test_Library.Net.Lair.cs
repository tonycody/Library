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
        public void Test_LairConverter_Document()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            _random.NextBytes(id);

            var tag = new Document(id, "aoeui");

            var stream = LairConverter.ToDocumentString(tag);
            var value = LairConverter.FromDocumentString(stream);

            Assert.AreEqual(tag, value, "LairConverter #4");
        }

        [Test]
        public void Test_LairConverter_Chat()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            _random.NextBytes(id);

            var tag = new Chat(id, "aoeui");

            var stream = LairConverter.ToChatString(tag);
            var value = LairConverter.FromChatString(stream);

            Assert.AreEqual(tag, value, "LairConverter #5");
        }

        [Test]
        public void Test_LairConverter_Whisper()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);
            var id = new byte[64];
            _random.NextBytes(id);

            var tag = new Whisper(id, "aoeui");

            WhisperCryptoInformation cryptoInformation1 = new WhisperCryptoInformation(CryptoAlgorithm.Rijndael256);
            var stream = LairConverter.ToWhisperString(tag, cryptoInformation1);

            WhisperCryptoInformation cryptoInformation2 = null;
            var value = LairConverter.FromWhisperString(stream, out cryptoInformation2);

            Assert.AreEqual(tag, value, "LairConverter #6");
            Assert.AreEqual(cryptoInformation1, cryptoInformation2, "LairConverter #7");
        }

        [Test]
        public void Test_ContentConverter_SignatureProfileContent()
        {
            Exchange exchange = new Exchange(ExchangeAlgorithm.Rsa2048);
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);

            var comment = RandomString.GetValue(SignatureProfileContent.MaxCommentLength);

            var signatures = new List<string>();
            signatures.Add(digitalSignature.ToString());

            var documents = new List<Document>();
            documents.Add(new Document(new byte[64], "123"));

            var chats = new List<Chat>();
            chats.Add(new Chat(new byte[64], "123"));

            SignatureProfileContent content = new SignatureProfileContent(comment, signatures, documents, chats, exchange);
            var binaryContent = ContentConverter.ToSignatureProfileContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromSignatureProfileContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_DocumentSiteContent()
        {
            List<DocumentPage> documentPages = new List<DocumentPage>();

            for (int i = 0; i < DocumentSiteContent.MaxDocumentPageCount; i++)
            {
                var hypertext = RandomString.GetValue(DocumentPage.MaxHypertextLength);
                var comment = RandomString.GetValue(DocumentPage.MaxCommentLength);

                documentPages.Add(new DocumentPage(HypertextFormatType.MiniWiki, hypertext, comment));
            }

            DocumentSiteContent content = new DocumentSiteContent(documentPages);
            var binaryContent = ContentConverter.ToDocumentSiteContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromDocumentSiteContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_DocumentOpinionContent()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.ECDsaP521_Sha512);

            var goods = new List<string>();
            var bads = new List<string>();

            for (int i = 0; i < DocumentOpinionContent.MaxGoodCount; i++)
            {
                goods.Add(digitalSignature.ToString());
            }

            for (int i = 0; i < DocumentOpinionContent.MaxBadCount; i++)
            {
                bads.Add(digitalSignature.ToString());
            }

            DocumentOpinionContent content = new DocumentOpinionContent(goods, bads);
            var binaryContent = ContentConverter.ToDocumentOpinionContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromDocumentOpinionContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_ChatTopicContent()
        {
            var comment = RandomString.GetValue(ChatTopicContent.MaxCommentLength);

            ChatTopicContent content = new ChatTopicContent(comment);
            var binaryContent = ContentConverter.ToChatTopicContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromChatTopicContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_ChatMessageContent()
        {
            var comment = RandomString.GetValue(ChatMessageContent.MaxCommentLength);

            var keys = new List<Key>();
            keys.Add(new Key(new byte[64], HashAlgorithm.Sha512));

            ChatMessageContent content = new ChatMessageContent(comment, keys);
            var binaryContent = ContentConverter.ToChatMessageContentBlock(content);

            Assert.AreEqual(content, ContentConverter.FromChatMessageContentBlock(binaryContent));
        }

        [Test]
        public void Test_ContentConverter_WhisperMessageContent()
        {
            var comment = RandomString.GetValue(WhisperMessageContent.MaxCommentLength);

            var keys = new List<Key>();
            keys.Add(new Key(new byte[64], HashAlgorithm.Sha512));

            WhisperCryptoInformation cryptoInformation = new WhisperCryptoInformation(CryptoAlgorithm.Rijndael256);

            WhisperMessageContent content = new WhisperMessageContent(comment, keys);
            var binaryContent = ContentConverter.ToWhisperMessageContentBlock(content, cryptoInformation);

            Assert.AreEqual(content, ContentConverter.FromWhisperMessageContentBlock(binaryContent, cryptoInformation));
        }

        [Test]
        public void Test_ContentConverter_MailMessageContent()
        {
            var comment = RandomString.GetValue(MailMessageContent.MaxCommentLength);

            Exchange exchange = new Exchange(ExchangeAlgorithm.Rsa2048);

            MailMessageContent content = new MailMessageContent(comment.ToString());
            var binaryContent = ContentConverter.ToMailMessageContentBlock(content, exchange);

            Assert.AreEqual(content, ContentConverter.FromMailMessageContentBlock(binaryContent, exchange));
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
