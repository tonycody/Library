using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class VoteCollection : FilterList<Vote>, IEnumerable<Vote>
    {
        public VoteCollection() : base() { }
        public VoteCollection(int capacity) : base(capacity) { }
        public VoteCollection(IEnumerable<Vote> collections) : base(collections) { }

        protected override bool Filter(Vote item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Vote>

        IEnumerator<Vote> IEnumerable<Vote>.GetEnumerator()
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
