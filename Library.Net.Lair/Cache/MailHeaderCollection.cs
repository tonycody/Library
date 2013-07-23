using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class MailHeaderCollection : FilterList<MailHeader>, IEnumerable<MailHeader>
    {
        public MailHeaderCollection() : base() { }
        public MailHeaderCollection(int capacity) : base(capacity) { }
        public MailHeaderCollection(IEnumerable<MailHeader> collections) : base(collections) { }

        protected override bool Filter(MailHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<MailHeader>

        IEnumerator<MailHeader> IEnumerable<MailHeader>.GetEnumerator()
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
