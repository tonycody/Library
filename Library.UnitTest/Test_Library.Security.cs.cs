using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Library.Security;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Security")]
    public class Test_Library_Security
    {
        private BufferManager _bufferManager = new BufferManager();

        [TearDown]
        public void TearDown()
        {
            _bufferManager.Dispose();
        }

        [Test]
        public void Test_DigitalSigunature()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.ECDsaP521_Sha512 })
            {
                DigitalSignature sigunature = new DigitalSignature("123", a);

                var streamSigunature = DigitalSignatureConverter.ToDigitalSignatureStream(sigunature);
                var sigunature2 = DigitalSignatureConverter.FromDigitalSignatureStream(streamSigunature);

                Assert.AreEqual(sigunature, sigunature2, "AmoebaConverter #4");
            }
        }
    }
}
