using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Security")]
    public class Test_Library_Security
    {
        [Test]
        public void Test_DigitalSigunature()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha512, DigitalSignatureAlgorithm.ECDsaP521_Sha512 })
            {
                DigitalSignature sigunature = new DigitalSignature("123", a);

                using (var streamSigunature = DigitalSignatureConverter.ToDigitalSignatureStream(sigunature))
                {
                    var sigunature2 = DigitalSignatureConverter.FromDigitalSignatureStream(streamSigunature);

                    Assert.AreEqual(sigunature, sigunature2, "AmoebaConverter #4");
                }
            }
        }
    }
}
