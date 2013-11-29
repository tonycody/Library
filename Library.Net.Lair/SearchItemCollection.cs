using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class SearchItemCollection : FilterList<SearchItem>, IEnumerable<SearchItem>
    {
        public SearchItemCollection() : base() { }
        public SearchItemCollection(int capacity) : base(capacity) { }
        public SearchItemCollection(IEnumerable<SearchItem> collections) : base(collections) { }

        protected override bool Filter(SearchItem item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SearchItem>

        IEnumerator<SearchItem> IEnumerable<SearchItem>.GetEnumerator()
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
