using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentPageCollection : FilterList<DocumentPage>, IEnumerable<DocumentPage>
    {
        public DocumentPageCollection() : base() { }
        public DocumentPageCollection(int capacity) : base(capacity) { }
        public DocumentPageCollection(IEnumerable<DocumentPage> collections) : base(collections) { }

        protected override bool Filter(DocumentPage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentPage>

        IEnumerator<DocumentPage> IEnumerable<DocumentPage>.GetEnumerator()
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
