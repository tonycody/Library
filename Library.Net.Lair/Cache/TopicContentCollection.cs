using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class TopicContentCollection : FilterList<TopicContent>, IEnumerable<TopicContent>
    {
        public TopicContentCollection() : base() { }
        public TopicContentCollection(int capacity) : base(capacity) { }
        public TopicContentCollection(IEnumerable<TopicContent> collections) : base(collections) { }

        protected override bool Filter(TopicContent item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<TopicContent>

        IEnumerator<TopicContent> IEnumerable<TopicContent>.GetEnumerator()
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
