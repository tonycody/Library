using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class LockedSortedStringSet : LockedSortedSet<string>
    {
        public LockedSortedStringSet()
            : base()
        {

        }

        public LockedSortedStringSet(IEnumerable<string> keys)
            : base(keys)
        {

        }
    }
}
