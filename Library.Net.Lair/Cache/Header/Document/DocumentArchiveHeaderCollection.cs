using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class DocumentArchiveHeaderCollection : FilterList<DocumentArchiveHeader>, IEnumerable<DocumentArchiveHeader>
    {
        public DocumentArchiveHeaderCollection() : base() { }
        public DocumentArchiveHeaderCollection(int capacity) : base(capacity) { }
        public DocumentArchiveHeaderCollection(IEnumerable<DocumentArchiveHeader> collections) : base(collections) { }

        protected override bool Filter(DocumentArchiveHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentArchiveHeader>

        IEnumerator<DocumentArchiveHeader> IEnumerable<DocumentArchiveHeader>.GetEnumerator()
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
