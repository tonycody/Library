using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class TagCollection : LockedList<Tag>
    {
        public TagCollection() : base() { }
        public TagCollection(int capacity) : base(capacity) { }
        public TagCollection(IEnumerable<Tag> collections) : base(collections) { }

        protected override bool Filter(Tag item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
