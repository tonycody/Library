using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class LeaderCollection : FilterList<Leader>, IEnumerable<Leader>
    {
        public LeaderCollection() : base() { }
        public LeaderCollection(int capacity) : base(capacity) { }
        public LeaderCollection(IEnumerable<Leader> collections) : base(collections) { }

        protected override bool Filter(Leader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Leader>

        IEnumerator<Leader> IEnumerable<Leader>.GetEnumerator()
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
