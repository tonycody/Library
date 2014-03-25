using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class LockedSortedStringDictionary<T> : LockedSortedDictionary<string, T>
    {
        public LockedSortedStringDictionary()
            : base()
        {

        }
    }
}
