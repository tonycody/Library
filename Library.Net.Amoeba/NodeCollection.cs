﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class NodeCollection : FilterList<Node>, IEnumerable<Node>
    {
        public NodeCollection() : base() { }
        public NodeCollection(int capacity) : base(capacity) { }
        public NodeCollection(IEnumerable<Node> collections) : base(collections) { }

        protected override bool Filter(Node item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
