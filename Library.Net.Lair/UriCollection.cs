using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class UriCollection : FilterList<string>, IEnumerable<string>
    {
        public UriCollection() : base() { }
        public UriCollection(int capacity) : base(capacity) { }
        public UriCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            const int i2pDestinationMaximumLength = 616;
            const int i2pWithSchemeMaximumLength = 4 + i2pDestinationMaximumLength;

            if (item == null
                || (item.Length > 256 && !item.StartsWith("i2p:"))
                || item.Length > i2pWithSchemeMaximumLength) return true;

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
