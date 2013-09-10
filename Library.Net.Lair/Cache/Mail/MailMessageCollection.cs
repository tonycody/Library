using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class MailMessageCollection : FilterList<MailMessage>, IEnumerable<MailMessage>
    {
        public MailMessageCollection() : base() { }
        public MailMessageCollection(int capacity) : base(capacity) { }
        public MailMessageCollection(IEnumerable<MailMessage> collections) : base(collections) { }

        protected override bool Filter(MailMessage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<MailMessage>

        IEnumerator<MailMessage> IEnumerable<MailMessage>.GetEnumerator()
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
