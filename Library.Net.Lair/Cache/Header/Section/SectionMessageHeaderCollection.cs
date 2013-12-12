using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class SectionMessageHeaderCollection : FilterList<SectionMessageHeader>, IEnumerable<SectionMessageHeader>
    {
        public SectionMessageHeaderCollection() : base() { }
        public SectionMessageHeaderCollection(int capacity) : base(capacity) { }
        public SectionMessageHeaderCollection(IEnumerable<SectionMessageHeader> collections) : base(collections) { }

        protected override bool Filter(SectionMessageHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SectionMessageHeader>

        IEnumerator<SectionMessageHeader> IEnumerable<SectionMessageHeader>.GetEnumerator()
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
