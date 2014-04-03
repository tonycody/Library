using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class UriCollection : LockedList<string>
    {
        public UriCollection() : base() { }
        public UriCollection(int capacity) : base(capacity) { }
        public UriCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxUriLength = 256;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > UriCollection.MaxUriLength) return true;

            return false;
        }
    }
}
