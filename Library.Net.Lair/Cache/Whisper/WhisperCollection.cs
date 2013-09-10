using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class WhisperCollection : FilterList<Whisper>, IEnumerable<Whisper>
    {
        public WhisperCollection() : base() { }
        public WhisperCollection(int capacity) : base(capacity) { }
        public WhisperCollection(IEnumerable<Whisper> collections) : base(collections) { }

        protected override bool Filter(Whisper item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Whisper>

        IEnumerator<Whisper> IEnumerable<Whisper>.GetEnumerator()
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
