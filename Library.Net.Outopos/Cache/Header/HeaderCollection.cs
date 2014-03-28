using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class HeaderCollection : LockedList<Header>
    {
        public HeaderCollection() : base() { }
        public HeaderCollection(int capacity) : base(capacity) { }
        public HeaderCollection(IEnumerable<Header> collections) : base(collections) { }

        protected override bool Filter(Header item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
