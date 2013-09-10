using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ChatMessageCollection : FilterList<ChatMessage>, IEnumerable<ChatMessage>
    {
        public ChatMessageCollection() : base() { }
        public ChatMessageCollection(int capacity) : base(capacity) { }
        public ChatMessageCollection(IEnumerable<ChatMessage> collections) : base(collections) { }

        protected override bool Filter(ChatMessage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ChatMessage>

        IEnumerator<ChatMessage> IEnumerable<ChatMessage>.GetEnumerator()
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
