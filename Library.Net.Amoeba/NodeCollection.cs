using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class NodeCollection : LockedList<Node>, IEnumerable<Node>
    {
        public NodeCollection() : base() { }
        public NodeCollection(int capacity) : base(capacity) { }
        public NodeCollection(IEnumerable<Node> collections) : base(collections) { }

        #region IEnumerable<Node> メンバ

        IEnumerator<Node> IEnumerable<Node>.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(base.ThisLock))
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable メンバ

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(base.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        #endregion
    }
}
