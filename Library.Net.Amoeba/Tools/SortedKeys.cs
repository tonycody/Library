using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class SortedKeys : SortedSet<Key>, IEnumerable<Key>
    {
        public SortedKeys()
            : base(new KeyComparer())
        {

        }

        class KeyComparer : IComparer<Key>
        {
            public int Compare(Key x, Key y)
            {
                int c = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
                if (c != 0) return c;

                // Unsafe
                if (Unsafe.Equals(x.Hash, y.Hash)) return 0;

                c = Collection.Compare(x.Hash, y.Hash);
                if (c != 0) return c;

                return 0;
            }
        }
    }
}
