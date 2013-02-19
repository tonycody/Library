using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
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

        #region IEnumerable<Node>

        IEnumerator<Node> IEnumerable<Node>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion
    }
}
