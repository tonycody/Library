using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class KeyCollection : LockedList<Key>, IEnumerable<Key>
    {
        public KeyCollection() : base() { }
        public KeyCollection(int capacity) : base(capacity) { }
        public KeyCollection(IEnumerable<Key> collections) : base(collections) { }

        #region IEnumerable<Header> メンバ

        IEnumerator<Key> IEnumerable<Key>.GetEnumerator()
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
