using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class KeyCollection : FilterList<Key>, IEnumerable<Key>
    {
        public KeyCollection() : base() { }
        public KeyCollection(int capacity) : base(capacity) { }
        public KeyCollection(IEnumerable<Key> collections) : base(collections) { }

        protected override bool Filter(Key item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Key>

        IEnumerator<Key> IEnumerable<Key>.GetEnumerator()
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
