using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class SeedCollection : FilterList<Seed>, IEnumerable<Seed>
    {
        public SeedCollection() : base() { }
        public SeedCollection(int capacity) : base(capacity) { }
        public SeedCollection(IEnumerable<Seed> collections) : base(collections) { }

        protected override bool Filter(Seed item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Seed>

        IEnumerator<Seed> IEnumerable<Seed>.GetEnumerator()
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
