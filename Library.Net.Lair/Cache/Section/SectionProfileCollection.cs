using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class SectionProfileCollection : FilterList<SectionProfile>, IEnumerable<SectionProfile>
    {
        public SectionProfileCollection() : base() { }
        public SectionProfileCollection(int capacity) : base(capacity) { }
        public SectionProfileCollection(IEnumerable<SectionProfile> collections) : base(collections) { }

        protected override bool Filter(SectionProfile item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SectionProfile>

        IEnumerator<SectionProfile> IEnumerable<SectionProfile>.GetEnumerator()
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
