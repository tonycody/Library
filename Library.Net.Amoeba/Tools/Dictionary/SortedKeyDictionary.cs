using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class SortedKeyDictionary<T> : SortedList<Key, T>
    {
        public SortedKeyDictionary()
            : base(new KeyComparer())
        {

        }
    }
}
