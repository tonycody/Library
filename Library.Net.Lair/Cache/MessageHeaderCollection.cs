using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class MessageHeaderCollection : FilterList<MessageHeader>, IEnumerable<MessageHeader>
    {
        public MessageHeaderCollection() : base() { }
        public MessageHeaderCollection(int capacity) : base(capacity) { }
        public MessageHeaderCollection(IEnumerable<MessageHeader> collections) : base(collections) { }

        protected override bool Filter(MessageHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<MessageHeader>

        IEnumerator<MessageHeader> IEnumerable<MessageHeader>.GetEnumerator()
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
