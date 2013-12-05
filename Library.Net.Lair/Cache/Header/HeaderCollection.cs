using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class HeaderCollection : FilterList<Header>, IEnumerable<Header>
    {
        public HeaderCollection() : base() { }
        public HeaderCollection(int capacity) : base(capacity) { }
        public HeaderCollection(IEnumerable<Header> collections) : base(collections) { }

        protected override bool Filter(Header item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Header>

        IEnumerator<Header> IEnumerable<Header>.GetEnumerator()
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
