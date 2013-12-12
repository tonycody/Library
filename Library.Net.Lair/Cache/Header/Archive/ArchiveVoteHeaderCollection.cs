using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class ArchiveVoteHeaderCollection : FilterList<ArchiveVoteHeader>, IEnumerable<ArchiveVoteHeader>
    {
        public ArchiveVoteHeaderCollection() : base() { }
        public ArchiveVoteHeaderCollection(int capacity) : base(capacity) { }
        public ArchiveVoteHeaderCollection(IEnumerable<ArchiveVoteHeader> collections) : base(collections) { }

        protected override bool Filter(ArchiveVoteHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ArchiveVoteHeader>

        IEnumerator<ArchiveVoteHeader> IEnumerable<ArchiveVoteHeader>.GetEnumerator()
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
