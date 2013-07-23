using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentContentCollection : FilterList<DocumentContent>, IEnumerable<DocumentContent>
    {
        public DocumentContentCollection() : base() { }
        public DocumentContentCollection(int capacity) : base(capacity) { }
        public DocumentContentCollection(IEnumerable<DocumentContent> collections) : base(collections) { }

        protected override bool Filter(DocumentContent item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentContent>

        IEnumerator<DocumentContent> IEnumerable<DocumentContent>.GetEnumerator()
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
