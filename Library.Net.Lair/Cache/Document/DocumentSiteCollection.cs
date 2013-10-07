using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentSiteCollection : FilterList<DocumentSite>, IEnumerable<DocumentSite>
    {
        public DocumentSiteCollection() : base() { }
        public DocumentSiteCollection(int capacity) : base(capacity) { }
        public DocumentSiteCollection(IEnumerable<DocumentSite> collections) : base(collections) { }

        protected override bool Filter(DocumentSite item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentSite>

        IEnumerator<DocumentSite> IEnumerable<DocumentSite>.GetEnumerator()
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
