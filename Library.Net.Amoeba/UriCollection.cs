using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class UriCollection : FilterList<string>, IEnumerable<string>
    {
        public UriCollection() : base() { }
        public UriCollection(int capacity) : base(capacity) { }
        public UriCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > 256) return true;

            return false;
        }

        #region IEnumerable<string>

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
