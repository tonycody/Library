using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ChannelCollection : FilterList<Channel>, IEnumerable<Channel>
    {
        public ChannelCollection() : base() { }
        public ChannelCollection(int capacity) : base(capacity) { }
        public ChannelCollection(IEnumerable<Channel> collections) : base(collections) { }

        protected override bool Filter(Channel item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Channel>

        IEnumerator<Channel> IEnumerable<Channel>.GetEnumerator()
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
