using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class UriCollection : FilterList<string>, IEnumerable<string>
    {
        public UriCollection() : base() { }
        public UriCollection(int capacity) : base(capacity) { }
        public UriCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > 256) return true;

            return false;
        }
    }
}
