using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    class KeyComparer : IComparer<Key>
    {
        public int Compare(Key x, Key y)
        {
            int c = x.GetHashCode().CompareTo(y.GetHashCode());
            if (c != 0) return c;

            c = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
            if (c != 0) return c;

            // Unsafe
            if (Unsafe.Equals(x.Hash, y.Hash)) return 0;

            c = Collection.Compare(x.Hash, y.Hash);
            if (c != 0) return c;

            return 0;
        }
    }
}
