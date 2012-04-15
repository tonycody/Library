using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class IndexCollection : LockedList<Index>, IEnumerable<Index>
    {
        public IndexCollection() : base() { }
        public IndexCollection(int capacity) : base(capacity) { }
        public IndexCollection(IEnumerable<Index> collections) : base(collections) { }

        #region IEnumerable<Index> メンバ

        IEnumerator<Index> IEnumerable<Index>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable メンバ

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
