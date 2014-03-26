using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class SortedKeySet : SortedSet<Key>
    {
        public SortedKeySet()
            : base(new KeyComparer())
        {

        }

        public SortedKeySet(IEnumerable<Key> keys)
            : base(new KeyComparer())
        {
            this.UnionWith(keys);
        }
    }
}
