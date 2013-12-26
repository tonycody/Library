using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class SectionCollection : FilterList<Section>, IEnumerable<Section>
    {
        public SectionCollection() : base() { }
        public SectionCollection(int capacity) : base(capacity) { }
        public SectionCollection(IEnumerable<Section> collections) : base(collections) { }

        protected override bool Filter(Section item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Section>

        IEnumerator<Section> IEnumerable<Section>.GetEnumerator()
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
