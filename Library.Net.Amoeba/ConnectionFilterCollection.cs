using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class ConnectionFilterCollection : FilterList<ConnectionFilter>, IEnumerable<ConnectionFilter>
    {
        public ConnectionFilterCollection() : base() { }
        public ConnectionFilterCollection(int capacity) : base(capacity) { }
        public ConnectionFilterCollection(IEnumerable<ConnectionFilter> collections) : base(collections) { }

        protected override bool Filter(ConnectionFilter item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
