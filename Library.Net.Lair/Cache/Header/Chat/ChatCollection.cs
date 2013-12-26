using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ChatCollection : FilterList<Chat>, IEnumerable<Chat>
    {
        public ChatCollection() : base() { }
        public ChatCollection(int capacity) : base(capacity) { }
        public ChatCollection(IEnumerable<Chat> collections) : base(collections) { }

        protected override bool Filter(Chat item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Chat>

        IEnumerator<Chat> IEnumerable<Chat>.GetEnumerator()
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
