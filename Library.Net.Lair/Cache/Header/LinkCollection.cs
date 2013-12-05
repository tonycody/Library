using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class LinkCollection : FilterList<Link>, IEnumerable<Link>
    {
        public LinkCollection() : base() { }
        public LinkCollection(int capacity) : base(capacity) { }
        public LinkCollection(IEnumerable<Link> collections) : base(collections) { }

        protected override bool Filter(Link item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Link>

        IEnumerator<Link> IEnumerable<Link>.GetEnumerator()
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
