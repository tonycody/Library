using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class AnchorCollection : LockedList<Anchor>, IEnumerable<Anchor>
    {
        public AnchorCollection() : base() { }
        public AnchorCollection(int capacity) : base(capacity) { }
        public AnchorCollection(IEnumerable<Anchor> collections) : base(collections) { }

        protected override bool Filter(Anchor item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Anchor>

        IEnumerator<Anchor> IEnumerable<Anchor>.GetEnumerator()
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
