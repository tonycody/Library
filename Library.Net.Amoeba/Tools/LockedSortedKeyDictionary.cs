using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class LockedSortedKeyDictionary<T> : LockedSortedDictionary<Key, T>
    {
        public LockedSortedKeyDictionary()
            : base(new KeyComparer())
        {

        }
    }
}
