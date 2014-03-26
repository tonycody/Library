using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class LockedSortedKeySet : LockedSortedSet<Key>
    {
        public LockedSortedKeySet()
            : base(new KeyComparer())
        {

        }

        public LockedSortedKeySet(IEnumerable<Key> keys)
            : base(new KeyComparer())
        {
            this.UnionWith(keys);
        }
    }
}
