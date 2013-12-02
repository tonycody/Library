using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class TagCollection : FilterList<Tag>, IEnumerable<Tag>
    {
        public TagCollection() : base() { }
        public TagCollection(int capacity) : base(capacity) { }
        public TagCollection(IEnumerable<Tag> collections) : base(collections) { }

        protected override bool Filter(Tag item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Tag>

        IEnumerator<Tag> IEnumerable<Tag>.GetEnumerator()
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
