using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Library.Net;
using Library.Net.Amoeba;
using NUnit.Framework;
using Library;

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

            {
                for (int i = 0; i < 1024; i++)
                {
                    var length = _random.Next(0, 1024);

                    byte[] x = new byte[length];
                    byte[] y = new byte[length];

                    _random.NextBytes(x);
                    _random.NextBytes(y);

                    var result = Test_Library_Net.Xor(x, y);

                    Type t = typeof(Kademlia<Node>);
                    var result2 = (byte[])t.InvokeMember("Xor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod, null, null, new object[] { x, y });

                    Assert.IsTrue(Collection.Equals(result, result2));
                }

                {
                    Type t = typeof(Kademlia<Node>);
                    var result = (byte[])t.InvokeMember("Xor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod, null, null, new object[] { new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 0, 4, 4 } });

                    Assert.IsTrue(Collection.Equals(result, new byte[] { 0, 5, 6, 3, 4 }));
                }
            }
        }

        private static byte[] Xor(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                byte[] buffer = new byte[Math.Max(x.Length, y.Length)];
                int length = Math.Min(x.Length, y.Length);

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                if (x.Length > y.Length)
                {
                    Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);
                }
                else
                {
                    Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);
                }

                return buffer;
            }
            else
            {
                byte[] buffer = new byte[x.Length];
                int length = x.Length;

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                return buffer;
            }
        }
    }
}
