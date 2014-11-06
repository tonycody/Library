using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Security")]
    public class Test_Library_Security
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_Sigunature()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha256, DigitalSignatureAlgorithm.EcDsaP521_Sha256 })
            {
                var signature = Signature.GetSignature(new DigitalSignature("123", a));

                Assert.IsTrue(Signature.Check(signature));
                Assert.AreEqual(Signature.GetNickname(signature), "123");
                Assert.IsTrue(Signature.GetHash(signature).Length == 32);
            }

            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha256, DigitalSignatureAlgorithm.EcDsaP521_Sha256 })
            {
                string signature;

                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(new byte[1024], 0, 1024);
                    signature = Signature.GetSignature(DigitalSignature.CreateCertificate(new DigitalSignature("123", a), stream));
                }

                Assert.IsTrue(Signature.Check(signature));
                Assert.AreEqual(Signature.GetNickname(signature), "123");
                Assert.IsTrue(Signature.GetHash(signature).Length == 32);
            }
        }

        [Test]
        public void Test_DigitalSigunature()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha256, DigitalSignatureAlgorithm.EcDsaP521_Sha256 })
            {
                DigitalSignature sigunature = new DigitalSignature("123", a);

                using (var streamSigunature = DigitalSignatureConverter.ToDigitalSignatureStream(sigunature))
                {
                    var sigunature2 = DigitalSignatureConverter.FromDigitalSignatureStream(streamSigunature);

                    Assert.AreEqual(sigunature, sigunature2, "AmoebaConverter #4");
                }
            }
        }

        [Test]
        public void Test_Exchange()
        {
            foreach (var a in new ExchangeAlgorithm[] { ExchangeAlgorithm.Rsa2048 })
            {
                Exchange exchange = new Exchange(a);

                byte[] buffer = new byte[128];
                _random.NextBytes(buffer);

                var eBuffer = Exchange.Encrypt(exchange.GetExchangePublicKey(), buffer);
                var dBuffer = Exchange.Decrypt(exchange.GetExchangePrivateKey(), eBuffer);

                Assert.IsTrue(CollectionUtilities.Equals(buffer, dBuffer), "Exchange #1");
            }
        }

        [Test]
        public void Test_Kdf()
        {
            Kdf kdf = new Kdf(System.Security.Cryptography.SHA256.Create());
            var value = kdf.GetBytes(new byte[4], 128);

            Assert.IsTrue(value.Length >= 128, "Kdf #1");
        }

        [Test]
        public void Test_Pbkdf2()
        {
            byte[] password = new byte[256];
            byte[] salt = new byte[256];

            _random.NextBytes(password);
            _random.NextBytes(salt);

            using (var hmac = new System.Security.Cryptography.HMACSHA1())
            {
                Pbkdf2 pbkdf2 = new Pbkdf2(hmac, password, salt, 1024);
                System.Security.Cryptography.Rfc2898DeriveBytes rfc2898DeriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 1024);

                Assert.IsTrue(CollectionUtilities.Equals(pbkdf2.GetBytes(1024), rfc2898DeriveBytes.GetBytes(1024)), "Pbkdf2 #1");
            }

            //_random.NextBytes(password);
            //_random.NextBytes(salt);

            //using (var hmac = new System.Security.Cryptography.HMACSHA256())
            //{
            //    CryptoConfig.AddAlgorithm(typeof(SHA256Cng),
            //        "SHA256",
            //        "SHA256Cng",
            //        "System.Security.Cryptography.SHA256",
            //        "System.Security.Cryptography.SHA256Cng");

            //    hmac.HashName = "System.Security.Cryptography.SHA256";

            //    Pbkdf2 pbkdf2 = new Pbkdf2(hmac, password, salt, 1024);
            //    var h = pbkdf2.GetBytes(10);
            //}
        }

        [Test]
        public void Test_Crc32_Castagnoli()
        {
            byte[] buffer = new byte[1024 * 32];
            _random.NextBytes(buffer);

            Assert.IsTrue(CollectionUtilities.Equals(T_Crc32_Castagnoli.ComputeHash(buffer), Crc32_Castagnoli.ComputeHash(buffer)));

            using (MemoryStream stream1 = new MemoryStream(buffer))
            using (MemoryStream stream2 = new MemoryStream(buffer))
            {
                Assert.IsTrue(CollectionUtilities.Equals(T_Crc32_Castagnoli.ComputeHash(stream1), Crc32_Castagnoli.ComputeHash(stream2)));
            }

            var list = new List<ArraySegment<byte>>();
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));

            Assert.IsTrue(CollectionUtilities.Equals(T_Crc32_Castagnoli.ComputeHash(list), Crc32_Castagnoli.ComputeHash(list)));
        }

        [Test]
        public void Test_HmacSha256()
        {
            // http://tools.ietf.org/html/rfc4868#section-2.7.1
            {
                var key = NetworkConverter.FromHexString("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
                var value = NetworkConverter.FromHexString("4869205468657265");

                using (MemoryStream stream = new MemoryStream(value))
                {
                    var s = NetworkConverter.ToHexString(HmacSha256.ComputeHash(stream, key));
                }

                using (HMACSHA256 hmacSha256 = new HMACSHA256(key))
                {
                    var s = NetworkConverter.ToHexString(hmacSha256.ComputeHash(value));
                }
            }

            var list = new List<int>();
            list.Add(1);
            list.Add(64);
            list.Add(128);

            byte[] buffer = new byte[1024 * 32];
            _random.NextBytes(buffer);

            for (int i = 0; i < list.Count; i++)
            {
                byte[] key = new byte[list[i]];
                _random.NextBytes(key);

                using (MemoryStream stream1 = new MemoryStream(buffer))
                using (MemoryStream stream2 = new MemoryStream(buffer))
                {
                    using (HMACSHA256 hmacSha256 = new HMACSHA256(key))
                    {
                        Assert.IsTrue(Unsafe.Equals(hmacSha256.ComputeHash(stream1), HmacSha256.ComputeHash(stream2, key)));
                    }
                }
            }
        }

        [Test]
        public void Test_Miner()
        {
            //{
            //    var key = NetworkConverter.FromHexString("e0ee19d617ee6ea9ea592afbdf71bafba6eecde2beba0d3cdc51419522fe5dbdf18f6830081be1615969b1fe43344fac3c312cd86a487cb1bd04f2c44cddca11");
            //    var value = NetworkConverter.FromHexString("01010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101");

            //    var count = Verify_1(key, value);
            //}

            {
                Miner miner = new Miner(CashAlgorithm.Version1, -1, new TimeSpan(0, 0, 1));

                Cash cash = null;

                Stopwatch sw = new Stopwatch();
                sw.Start();

                using (MemoryStream stream = new MemoryStream(NetworkConverter.FromHexString("0101010101010101")))
                {
                    cash = miner.Create(stream);
                }

                sw.Stop();
                Assert.IsTrue(sw.ElapsedMilliseconds < 1000 * 30);
            }

            {
                Miner miner = new Miner(CashAlgorithm.Version1, 20, new TimeSpan(1, 0, 0));

                Cash cash = null;

                using (MemoryStream stream = new MemoryStream(NetworkConverter.FromHexString("0101010101010101")))
                {
                    cash = miner.Create(stream);

                    stream.Seek(0, SeekOrigin.Begin);
                    Assert.IsTrue(Miner.Verify(cash, stream) >= 20);
                }
            }

            {
                Miner miner = new Miner(CashAlgorithm.Version1, 0, TimeSpan.Zero);

                Cash cash = null;

                using (MemoryStream stream = new MemoryStream(NetworkConverter.FromHexString("0101010101010101")))
                {
                    cash = miner.Create(stream);
                }

                Assert.IsTrue(cash == null);
            }

            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Assert.Throws<AggregateException>(() =>
                {
                    Miner miner = new Miner(CashAlgorithm.Version1, -1, new TimeSpan(1, 0, 0));

                    var task = Task.Factory.StartNew(() =>
                    {
                        Cash cash = null;

                        using (MemoryStream stream = new MemoryStream(NetworkConverter.FromHexString("0101010101010101")))
                        {
                            cash = miner.Create(stream);
                        }
                    });

                    Thread.Sleep(1000);

                    miner.Cancel();

                    task.Wait();
                });

                sw.Stop();
                Assert.IsTrue(sw.ElapsedMilliseconds < 1000 * 30);
            }
        }

        //public int Verify_1(byte[] key, byte[] value)
        //{
        //    if (key == null) throw new ArgumentNullException("key");
        //    if (key.Length != 64) throw new ArgumentOutOfRangeException("key");
        //    if (value == null) throw new ArgumentNullException("value");
        //    if (value.Length != 64) throw new ArgumentOutOfRangeException("value");

        //    var bufferManager = BufferManager.Instance;

        //    try
        //    {

        //        byte[] result;

        //        {
        //            byte[] buffer = bufferManager.TakeBuffer(128);
        //            Unsafe.Copy(key, 0, buffer, 0, 64);
        //            Unsafe.Copy(value, 0, buffer, 64, 64);

        //            result = Sha256.ComputeHash(buffer, 0, 128);

        //            bufferManager.ReturnBuffer(buffer);
        //        }

        //        int count = 0;

        //        for (int i = 0; i < 64; i++)
        //        {
        //            for (int j = 0; j < 8; j++)
        //            {
        //                if (((result[i] << j) & 0x80) == 0) count++;
        //                else goto End;
        //            }
        //        }
        //    End:

        //        return count;
        //    }
        //    catch (Exception)
        //    {
        //        return 0;
        //    }
        //}
    }
}
