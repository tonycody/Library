﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Library.Net;
using Library.Net.Amoeba;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net")]
    public class Test_Library_Net
    {
        [Test]
        public void Test_Kademlia()
        {
            var kademlia = new Kademlia<Node>(512, 20);
            var random = new Random();

            {
                var node = new Node();
                var id = new byte[64];
                random.NextBytes(id);
                node.Id = id;

                kademlia.BaseNode = node;
            }

            for (int i = 0; i < 1024; i++)
            {
                var node = new Node();
                var id = new byte[64];
                random.NextBytes(id);
                node.Id = id;

                kademlia.Add(node);
            }

            for (int i = 0; i < 1024; i++)
            {
                var node = new Node();
                var id = new byte[64];
                random.NextBytes(id);
                node.Id = id;

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
                var tnode = new Node();
                var id = new byte[64];
                random.NextBytes(id);
                tnode.Id = id;

                var slist = Kademlia<Node>.Sort(kademlia.BaseNode, tnode.Id, kademlia.ToArray());
            }
        }
    }
}