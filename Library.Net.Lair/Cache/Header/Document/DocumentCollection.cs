using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentCollection : FilterList<Document>, IEnumerable<Document>
    {
        public DocumentCollection() : base() { }
        public DocumentCollection(int capacity) : base(capacity) { }
        public DocumentCollection(IEnumerable<Document> collections) : base(collections) { }

        protected override bool Filter(Document item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Document>

        IEnumerator<Document> IEnumerable<Document>.GetEnumerator()
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
