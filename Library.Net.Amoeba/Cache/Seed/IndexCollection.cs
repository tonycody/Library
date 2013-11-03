using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class IndexCollection : FilterList<Index>, IEnumerable<Index>
    {
        public IndexCollection() : base() { }
        public IndexCollection(int capacity) : base(capacity) { }
        public IndexCollection(IEnumerable<Index> collections) : base(collections) { }

        protected override bool Filter(Index item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
