using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    public sealed class BoxCollection : FilterList<Box>, IEnumerable<Box>
    {
        public BoxCollection() : base() { }
        public BoxCollection(int capacity) : base(capacity) { }
        public BoxCollection(IEnumerable<Box> collections) : base(collections) { }

        protected override bool Filter(Box item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Box>

        IEnumerator<Box> IEnumerable<Box>.GetEnumerator()
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
