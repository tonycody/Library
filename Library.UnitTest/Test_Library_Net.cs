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
            {
                var kademlia = new Kademlia<Node>(512, 20);

                {
                    Node node = null;

                    {
                        var id = new byte[32];
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
                        var id = new byte[32];
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
                        var id = new byte[32];
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
                    var list = kademlia.Search(node.Id, kademlia.Count);
                    if (list.ElementAt(0) != node) throw new Exception();
                }

                for (int i = 0; i < 1024; i++)
                {
                    var id = new byte[32];
                    _random.NextBytes(id);

                    var slist = Kademlia<Node>.Search(kademlia.BaseNode.Id, id, kademlia.ToArray(), kademlia.Count).ToList();
                    var slist2 = Kademlia<Node>.Search(kademlia.BaseNode.Id, id, kademlia.ToArray(), 3).ToList();

                    if (slist.Count == 0 && slist2.Count == 0) continue;
                    var length = Math.Min(slist.Count, slist2.Count);

                    Assert.IsTrue(CollectionUtilities.Equals(slist, 0, slist2, 0, length));
                }

                for (int i = 0; i < 1024; i++)
                {
                    var id = new byte[32];
                    _random.NextBytes(id);

                    var slist = Kademlia<Node>.Search(id, kademlia.ToArray(), kademlia.Count).ToList();
                    var slist2 = Kademlia<Node>.Search(id, kademlia.ToArray(), 3).ToList();

                    if (slist.Count == 0 && slist2.Count == 0) continue;
                    var length = Math.Min(slist.Count, slist2.Count);

                    Assert.IsTrue(CollectionUtilities.Equals(slist, 0, slist2, 0, length));
                }
            }
        }
    }
}
