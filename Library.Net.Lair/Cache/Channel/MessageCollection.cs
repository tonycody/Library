using System.Collections.Generic;

namespace Library.Net.Lair
{
    public sealed class MessageCollection : FilterList<Message>, IEnumerable<Message>
    {
        public MessageCollection() : base() { }
        public MessageCollection(int capacity) : base(capacity) { }
        public MessageCollection(IEnumerable<Message> collections) : base(collections) { }

        protected override bool Filter(Message item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Message>

        IEnumerator<Message> IEnumerable<Message>.GetEnumerator()
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
