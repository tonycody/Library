using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class ChatMessageHeaderCollection : FilterList<ChatMessageHeader>, IEnumerable<ChatMessageHeader>
    {
        public ChatMessageHeaderCollection() : base() { }
        public ChatMessageHeaderCollection(int capacity) : base(capacity) { }
        public ChatMessageHeaderCollection(IEnumerable<ChatMessageHeader> collections) : base(collections) { }

        protected override bool Filter(ChatMessageHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ChatMessageHeader>

        IEnumerator<ChatMessageHeader> IEnumerable<ChatMessageHeader>.GetEnumerator()
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
