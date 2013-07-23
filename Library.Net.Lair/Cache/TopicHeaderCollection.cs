using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class TopicHeaderCollection : FilterList<TopicHeader>, IEnumerable<TopicHeader>
    {
        public TopicHeaderCollection() : base() { }
        public TopicHeaderCollection(int capacity) : base(capacity) { }
        public TopicHeaderCollection(IEnumerable<TopicHeader> collections) : base(collections) { }

        protected override bool Filter(TopicHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<TopicHeader>

        IEnumerator<TopicHeader> IEnumerable<TopicHeader>.GetEnumerator()
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
