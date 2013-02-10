using System.Collections.Generic;

namespace Library.Net.Lair
{
    public sealed class BoardCollection : FilterList<Board>, IEnumerable<Board>
    {
        public BoardCollection() : base() { }
        public BoardCollection(int capacity) : base(capacity) { }
        public BoardCollection(IEnumerable<Board> collections) : base(collections) { }

        protected override bool Filter(Board item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Board>

        IEnumerator<Board> IEnumerable<Board>.GetEnumerator()
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
