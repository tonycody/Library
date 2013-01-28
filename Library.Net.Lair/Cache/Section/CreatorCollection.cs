using System.Collections.Generic;

namespace Library.Net.Lair
{
    public sealed class CreatorCollection : FilterList<Creator>, IEnumerable<Creator>
    {
        public CreatorCollection() : base() { }
        public CreatorCollection(int capacity) : base(capacity) { }
        public CreatorCollection(IEnumerable<Creator> collections) : base(collections) { }

        protected override bool Filter(Creator item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Creator>

        IEnumerator<Creator> IEnumerable<Creator>.GetEnumerator()
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
