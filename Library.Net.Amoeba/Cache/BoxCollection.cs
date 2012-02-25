using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class BoxCollection : LockedList<Box>, IEnumerable<Box>
    {
        public BoxCollection() : base() { }
        public BoxCollection(int capacity) : base(capacity) { }
        public BoxCollection(IEnumerable<Box> collections) : base(collections) { }

        #region IEnumerable<Directory> メンバ

        IEnumerator<Box> IEnumerable<Box>.GetEnumerator()
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
