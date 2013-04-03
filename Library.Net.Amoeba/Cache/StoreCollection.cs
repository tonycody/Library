using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class StoreCollection : FilterList<Store>, IEnumerable<Store>
    {
        public StoreCollection() : base() { }
        public StoreCollection(int capacity) : base(capacity) { }
        public StoreCollection(IEnumerable<Store> collections) : base(collections) { }

        protected override bool Filter(Store item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Store>

        IEnumerator<Store> IEnumerable<Store>.GetEnumerator()
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
