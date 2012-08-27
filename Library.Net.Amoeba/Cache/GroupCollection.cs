using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class GroupCollection : FilterList<Group>, IEnumerable<Group>
    {
        public GroupCollection() : base() { }
        public GroupCollection(int capacity) : base(capacity) { }
        public GroupCollection(IEnumerable<Group> collections) : base(collections) { }

        protected override bool Filter(Group item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Group>

        IEnumerator<Group> IEnumerable<Group>.GetEnumerator()
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
