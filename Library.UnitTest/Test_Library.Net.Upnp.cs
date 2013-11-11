using System;
using Library.Net.Upnp;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Upnp")]
    public class Test_Library_Net_Upnp
    {
        [Test]
        public void Test_UpnpClient()
        {
            UpnpClient client = new UpnpClient();
            client.Connect(new TimeSpan(0, 0, 10));

            var ip = client.GetExternalIpAddress(new TimeSpan(0, 0, 10));
            Assert.AreNotEqual(ip, null, "UPnPClient #1");
        }
    }
}
