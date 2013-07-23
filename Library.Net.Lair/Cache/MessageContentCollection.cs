using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class MessageContentCollection : FilterList<MessageContent>, IEnumerable<MessageContent>
    {
        public MessageContentCollection() : base() { }
        public MessageContentCollection(int capacity) : base(capacity) { }
        public MessageContentCollection(IEnumerable<MessageContent> collections) : base(collections) { }

        protected override bool Filter(MessageContent item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<MessageContent>

        IEnumerator<MessageContent> IEnumerable<MessageContent>.GetEnumerator()
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
