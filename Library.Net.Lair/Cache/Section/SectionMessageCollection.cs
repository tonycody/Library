using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class SectionMessageCollection : FilterList<SectionMessage>, IEnumerable<SectionMessage>
    {
        public SectionMessageCollection() : base() { }
        public SectionMessageCollection(int capacity) : base(capacity) { }
        public SectionMessageCollection(IEnumerable<SectionMessage> collections) : base(collections) { }

        protected override bool Filter(SectionMessage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SectionMessage>

        IEnumerator<SectionMessage> IEnumerable<SectionMessage>.GetEnumerator()
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
