using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class SeedCollection : LockedList<Seed>, IEnumerable<Seed>
    {
        public SeedCollection() : base() { }
        public SeedCollection(int capacity) : base(capacity) { }
        public SeedCollection(IEnumerable<Seed> collections) : base(collections) { }

        #region IEnumerable<Key> メンバ

        IEnumerator<Seed> IEnumerable<Seed>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable メンバ

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
