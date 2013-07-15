using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class KeywordCollection : FilterList<string>, IEnumerable<string>
    {
        public KeywordCollection() : base() { }
        public KeywordCollection(int capacity) : base(capacity) { }
        public KeywordCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > 256) return true;

            return false;
        }

        #region IEnumerable<Keyword>

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
