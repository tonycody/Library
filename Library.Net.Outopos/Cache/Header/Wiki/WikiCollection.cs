using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class WikiCollection : FilterList<Wiki>, IEnumerable<Wiki>
    {
        public WikiCollection() : base() { }
        public WikiCollection(int capacity) : base(capacity) { }
        public WikiCollection(IEnumerable<Wiki> collections) : base(collections) { }

        protected override bool Filter(Wiki item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Wiki>

        IEnumerator<Wiki> IEnumerable<Wiki>.GetEnumerator()
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
