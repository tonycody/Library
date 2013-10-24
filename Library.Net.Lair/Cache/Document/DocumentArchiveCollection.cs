using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentArchiveCollection : FilterList<DocumentArchive>, IEnumerable<DocumentArchive>
    {
        public DocumentArchiveCollection() : base() { }
        public DocumentArchiveCollection(int capacity) : base(capacity) { }
        public DocumentArchiveCollection(IEnumerable<DocumentArchive> collections) : base(collections) { }

        protected override bool Filter(DocumentArchive item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentSite>

        IEnumerator<DocumentArchive> IEnumerable<DocumentArchive>.GetEnumerator()
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
