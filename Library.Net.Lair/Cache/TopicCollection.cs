using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class TopicCollection : FilterList<Topic>, IEnumerable<Topic>
    {
        public TopicCollection() : base() { }
        public TopicCollection(int capacity) : base(capacity) { }
        public TopicCollection(IEnumerable<Topic> collections) : base(collections) { }

        protected override bool Filter(Topic item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Topic>

        IEnumerator<Topic> IEnumerable<Topic>.GetEnumerator()
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
