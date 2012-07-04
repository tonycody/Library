using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public class FilterCollection : FilterList<Filter>, IEnumerable<Filter>
    {
        public FilterCollection() : base() { }
        public FilterCollection(int capacity) : base(capacity) { }
        public FilterCollection(IEnumerable<Filter> collections) : base(collections) { }

        protected override bool Filter(Filter item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Filter>

        IEnumerator<Filter> IEnumerable<Filter>.GetEnumerator()
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
