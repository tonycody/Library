using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class LinkCollection : FilterList<string>, IEnumerable<string>
    {
        public LinkCollection() : base() { }
        public LinkCollection(int capacity) : base(capacity) { }
        public LinkCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxLinkLength = 1024;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > LinkCollection.MaxLinkLength) return true;

            return false;
        }

        #region IEnumerable<Link>

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
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
