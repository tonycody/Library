using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;
using Library;

namespace Library.Net.Rosa
{
    public class IdCollection : LockedList<byte[]>, IEnumerable<byte[]>
    {
        public IdCollection() : base() { }
        public IdCollection(int capacity) : base(capacity) { }
        public IdCollection(IEnumerable<byte[]> collections) : base(collections) { }

        #region IEnumerable<byte[]> メンバ

        IEnumerator<byte[]> IEnumerable<byte[]>.GetEnumerator()
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
