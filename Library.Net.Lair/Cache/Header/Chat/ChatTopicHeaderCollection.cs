using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class ChatTopicHeaderCollection : FilterList<ChatTopicHeader>, IEnumerable<ChatTopicHeader>
    {
        public ChatTopicHeaderCollection() : base() { }
        public ChatTopicHeaderCollection(int capacity) : base(capacity) { }
        public ChatTopicHeaderCollection(IEnumerable<ChatTopicHeader> collections) : base(collections) { }

        protected override bool Filter(ChatTopicHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ChatTopicHeader>

        IEnumerator<ChatTopicHeader> IEnumerable<ChatTopicHeader>.GetEnumerator()
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
