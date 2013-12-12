using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class DocumentVoteHeaderCollection : FilterList<DocumentVoteHeader>, IEnumerable<DocumentVoteHeader>
    {
        public DocumentVoteHeaderCollection() : base() { }
        public DocumentVoteHeaderCollection(int capacity) : base(capacity) { }
        public DocumentVoteHeaderCollection(IEnumerable<DocumentVoteHeader> collections) : base(collections) { }

        protected override bool Filter(DocumentVoteHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentVoteHeader>

        IEnumerator<DocumentVoteHeader> IEnumerable<DocumentVoteHeader>.GetEnumerator()
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
