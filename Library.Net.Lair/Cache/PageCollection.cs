using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class PageCollection : FilterList<Page>, IEnumerable<Page>
    {
        public PageCollection() : base() { }
        public PageCollection(int capacity) : base(capacity) { }
        public PageCollection(IEnumerable<Page> collections) : base(collections) { }

        protected override bool Filter(Page item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Page>

        IEnumerator<Page> IEnumerable<Page>.GetEnumerator()
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
