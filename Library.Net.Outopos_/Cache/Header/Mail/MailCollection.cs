using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class MailCollection : LockedList<Mail>, IEnumerable<Mail>
    {
        public MailCollection() : base() { }
        public MailCollection(int capacity) : base(capacity) { }
        public MailCollection(IEnumerable<Mail> collections) : base(collections) { }

        protected override bool Filter(Mail item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Mail>

        IEnumerator<Mail> IEnumerable<Mail>.GetEnumerator()
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
