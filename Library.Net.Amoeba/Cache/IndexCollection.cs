using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class IndexCollection : FilterList<Index>, IEnumerable<Index>
    {
        public IndexCollection() : base() { }
        public IndexCollection(int capacity) : base(capacity) { }
        public IndexCollection(IEnumerable<Index> collections) : base(collections) { }

        protected override bool Filter(Index item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Index>

        IEnumerator<Index> IEnumerable<Index>.GetEnumerator()
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
