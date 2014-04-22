using System;
using System.Collections.Generic;
using System.IO;
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

                Assert.IsTrue(Signature.HasSignature(signature));
                Assert.AreEqual(Signature.GetSignatureNickname(signature), "123");
                Assert.IsTrue(Signature.GetSignatureHash(signature).Length == 64);
            }

            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.EcDsaP521_Sha512 })
            {
                string signature;

                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(new byte[1024], 0, 1024);
                    signature = Signature.GetSignature(DigitalSignature.CreateCertificate(new DigitalSignature("123", a), stream));
                }

                Assert.IsTrue(Signature.HasSignature(signature));
                Assert.AreEqual(Signature.GetSignatureNickname(signature), "123");
                Assert.IsTrue(Signature.GetSignatureHash(signature).Length == 64);
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

                Assert.IsTrue(Collection.Equals(buffer, dBuffer), "Exchange #1");
            }
        }

        [Test]
        public void Test_Pbkdf2()
        {
            byte[] password = new byte[256];
            byte[] salt = new byte[256];

            _random.NextBytes(password);
            _random.NextBytes(salt);

            Pbkdf2 pbkdf2 = new Pbkdf2(new System.Security.Cryptography.HMACSHA1(), password, salt, 1024);
            System.Security.Cryptography.Rfc2898DeriveBytes rfc2898DeriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 1024);

            Assert.IsTrue(Collection.Equals(pbkdf2.GetBytes(1024), rfc2898DeriveBytes.GetBytes(1024)), "Pbkdf2 #1");
        }

        [Test]
        public void Test_Crc32_Castagnoli()
        {
            byte[] buffer = new byte[1024 * 32];
            _random.NextBytes(buffer);

            Assert.IsTrue(Collection.Equals(T_Crc32_Castagnoli.ComputeHash(buffer), Crc32_Castagnoli.ComputeHash(buffer)));

            using (MemoryStream stream1 = new MemoryStream(buffer))
            using (MemoryStream stream2 = new MemoryStream(buffer))
            {
                Assert.IsTrue(Collection.Equals(T_Crc32_Castagnoli.ComputeHash(stream1), Crc32_Castagnoli.ComputeHash(stream2)));
            }

            var list = new List<ArraySegment<byte>>();
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));
            list.Add(new ArraySegment<byte>(buffer));

            Assert.IsTrue(Collection.Equals(T_Crc32_Castagnoli.ComputeHash(list), Crc32_Castagnoli.ComputeHash(list)));
        }
    }
}
