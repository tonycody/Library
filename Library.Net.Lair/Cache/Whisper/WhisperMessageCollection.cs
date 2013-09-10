using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class WhisperMessageCollection : FilterList<WhisperMessage>, IEnumerable<WhisperMessage>
    {
        public WhisperMessageCollection() : base() { }
        public WhisperMessageCollection(int capacity) : base(capacity) { }
        public WhisperMessageCollection(IEnumerable<WhisperMessage> collections) : base(collections) { }

        protected override bool Filter(WhisperMessage item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<WhisperMessage>

        IEnumerator<WhisperMessage> IEnumerable<WhisperMessage>.GetEnumerator()
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
