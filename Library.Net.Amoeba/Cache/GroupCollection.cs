using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class GroupCollection : LockedList<Group>, IEnumerable<Group>
    {
        public GroupCollection() : base() { }
        public GroupCollection(int capacity) : base(capacity) { }
        public GroupCollection(IEnumerable<Group> collections) : base(collections) { }

        #region IEnumerable<Group> メンバ

        IEnumerator<Group> IEnumerable<Group>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable メンバ

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
