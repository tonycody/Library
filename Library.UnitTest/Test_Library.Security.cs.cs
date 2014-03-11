using System;
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
    }
}
