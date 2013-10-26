using System;
using System.Linq;
using Library.Net;
using Library.Net.Amoeba;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net")]
    public class Test_Library_Net
    {
        private Random _random = new Random();

        [Test]
        public void Test_Kademlia()
        {
            var kademlia = new Kademlia<Node>(512, 20);

            {
                Node node = null;

                {
                    var id = new byte[64];
                    _random.NextBytes(id);
                    var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                    node = new Node(id, uris);
                }

                kademlia.BaseNode = node;
            }

            for (int i = 0; i < 1024; i++)
            {
                Node node = null;

                {
                    var id = new byte[64];
                    _random.NextBytes(id);
                    var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                    node = new Node(id, uris);
                }

                kademlia.Add(node);
            }

            for (int i = 0; i < 1024; i++)
            {
                Node node = null;

                {
                    var id = new byte[64];
                    _random.NextBytes(id);
                    var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                    node = new Node(id, uris);
                }

                kademlia.Live(node);
            }

            foreach (var node in kademlia.ToArray().Take(kademlia.Count / 2))
            {
                kademlia.Remove(node);
            }

            var v = kademlia.Verify();

            foreach (var node in kademlia.ToArray())
            {
                var list = kademlia.Search(node.Id);
                if (list.ElementAt(0) != node) throw new Exception();
            }

            {
                var id = new byte[64];
                _random.NextBytes(id);

                var slist = Kademlia<Node>.Sort(kademlia.BaseNode.Id, id, kademlia.ToArray());
            }
        }
    }
}
