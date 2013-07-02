using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ArchiveCollection : FilterList<Archive>, IEnumerable<Archive>
    {
        public ArchiveCollection() : base() { }
        public ArchiveCollection(int capacity) : base(capacity) { }
        public ArchiveCollection(IEnumerable<Archive> collections) : base(collections) { }

        protected override bool Filter(Archive item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Archive>

        IEnumerator<Archive> IEnumerable<Archive>.GetEnumerator()
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
