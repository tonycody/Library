using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class WikiPageCollection : LockedList<WikiPage>, IEnumerable<WikiPage>
    {
        public WikiPageCollection() : base() { }
        public WikiPageCollection(int capacity) : base(capacity) { }
        public WikiPageCollection(IEnumerable<WikiPage> collections) : base(collections) { }

        protected override bool Filter(WikiPage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<WikiPage>

        IEnumerator<WikiPage> IEnumerable<WikiPage>.GetEnumerator()
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
