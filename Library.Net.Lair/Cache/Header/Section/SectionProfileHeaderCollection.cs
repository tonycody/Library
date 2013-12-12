using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class SectionProfileHeaderCollection : FilterList<SectionProfileHeader>, IEnumerable<SectionProfileHeader>
    {
        public SectionProfileHeaderCollection() : base() { }
        public SectionProfileHeaderCollection(int capacity) : base(capacity) { }
        public SectionProfileHeaderCollection(IEnumerable<SectionProfileHeader> collections) : base(collections) { }

        protected override bool Filter(SectionProfileHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SectionProfileHeader>

        IEnumerator<SectionProfileHeader> IEnumerable<SectionProfileHeader>.GetEnumerator()
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
