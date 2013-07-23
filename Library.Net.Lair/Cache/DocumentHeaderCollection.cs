using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentHeaderCollection : FilterList<DocumentHeader>, IEnumerable<DocumentHeader>
    {
        public DocumentHeaderCollection() : base() { }
        public DocumentHeaderCollection(int capacity) : base(capacity) { }
        public DocumentHeaderCollection(IEnumerable<DocumentHeader> collections) : base(collections) { }

        protected override bool Filter(DocumentHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentHeader>

        IEnumerator<DocumentHeader> IEnumerable<DocumentHeader>.GetEnumerator()
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
