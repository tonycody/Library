using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ChatTopicCollection : FilterList<ChatTopic>, IEnumerable<ChatTopic>
    {
        public ChatTopicCollection() : base() { }
        public ChatTopicCollection(int capacity) : base(capacity) { }
        public ChatTopicCollection(IEnumerable<ChatTopic> collections) : base(collections) { }

        protected override bool Filter(ChatTopic item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ChatTopic>

        IEnumerator<ChatTopic> IEnumerable<ChatTopic>.GetEnumerator()
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
