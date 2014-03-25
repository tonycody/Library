using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class GroupCollection : LockedList<Group>
    {
        public GroupCollection() : base() { }
        public GroupCollection(int capacity) : base(capacity) { }
        public GroupCollection(IEnumerable<Group> collections) : base(collections) { }

        protected override bool Filter(Group item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
