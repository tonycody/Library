using System;
using Library.Net.Amoeba;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Amoeba")]
    public class Test_Library_Net_Amoeba
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_AmoebaConverter_Node()
        {
            var node = new Node();
            var id = new byte[64];
            _random.NextBytes(id);
            node.Id = id;
            node.Uris.AddRange(new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" });

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
    }
}
