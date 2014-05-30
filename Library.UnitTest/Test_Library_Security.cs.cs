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
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.EcDsaP521_Sha512 })
            {
                var signature = Signature.GetSignature(new DigitalSignature("123", a));

                Assert.IsTrue(Signature.Check(signature));
                Assert.AreEqual(Signature.GetNickname(signature), "123");
                Assert.IsTrue(Signature.GetHash(signature).Length == 64);
            }

            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.EcDsaP521_Sha512 })
            {
                string signature;

                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(new byte[1024], 0, 1024);
                    signature = Signature.GetSignature(DigitalSignature.CreateCertificate(new DigitalSignature("123", a), stream));
                }

                Assert.IsTrue(Signature.Check(signature));
                Assert.AreEqual(Signature.GetNickname(signature), "123");
                Assert.IsTrue(Signature.GetHash(signature).Length == 64);
            }
        }

        [Test]
        public void Test_DigitalSigunature()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.EcDsaP521_Sha512 })
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

                var eBuffer = Exchange.Encrypt(exchange.GetPublicKey(), buffer);
                var dBuffer = Exchange.Decrypt(exchange.GetPrivateKey(), eBuffer);

                Assert.IsTrue(CollectionUtilities.Equals(buffer, dBuffer), "Exchange #1");
            }
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

            //using (var hmac = new System.Security.Cryptography.HMACSHA512())
            //{
            //    CryptoConfig.AddAlgorithm(typeof(SHA512Cng),
            //        "SHA512",
            //        "SHA512Cng",
            //        "System.Security.Cryptography.SHA512",
            //        "System.Security.Cryptography.SHA512Cng");

            //    hmac.HashName = "System.Security.Cryptography.SHA512";

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
        public void Test_HmacSha512()
        {
            // http://tools.ietf.org/html/rfc4868#section-2.7.1
            {
                var key = NetworkConverter.FromHexString("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
                var value = NetworkConverter.FromHexString("4869205468657265");

                using (MemoryStream stream = new MemoryStream(value))
                {
                    var s = NetworkConverter.ToHexString(HmacSha512.ComputeHash(stream, key));
                }

                using (HMACSHA512 hmacSha512 = new HMACSHA512(key))
                {
                    var s = NetworkConverter.ToHexString(hmacSha512.ComputeHash(value));
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
                    using (HMACSHA512 hmacSha512 = new HMACSHA512(key))
                    {
                        Assert.IsTrue(Unsafe.Equals(hmacSha512.ComputeHash(stream1), HmacSha512.ComputeHash(stream2, key)));
                    }
                }
            }
        }

        [Test]
        public void Test_Cash()
        {
            {
                var cash = Cash.Create(CashAlgorithm.Version1, NetworkConverter.FromHexString("0101010101010101"), new TimeSpan(0, 0, 30));
                var count = cash.Verify(NetworkConverter.FromHexString("0101010101010101"));
                Assert.IsTrue(count > 4);
            }

            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                var task = Task.Factory.StartNew(() =>
                {
                    var cash = Cash.Create(CashAlgorithm.Version1, NetworkConverter.FromHexString("0101010101010101"), new TimeSpan(0, 0, 0));
                });

                Thread.Sleep(1000);

                task.Wait();

                sw.Stop();
                Assert.IsTrue(sw.ElapsedMilliseconds < 1000 * 3);
            }
        }
    }
}
