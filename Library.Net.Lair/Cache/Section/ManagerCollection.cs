using System.Collections.Generic;

namespace Library.Net.Lair
{
    public sealed class ManagerCollection : FilterList<Manager>, IEnumerable<Manager>
    {
        public ManagerCollection() : base() { }
        public ManagerCollection(int capacity) : base(capacity) { }
        public ManagerCollection(IEnumerable<Manager> collections) : base(collections) { }

        protected override bool Filter(Manager item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Manager>

        IEnumerator<Manager> IEnumerable<Manager>.GetEnumerator()
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
