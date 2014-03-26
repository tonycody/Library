using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class SortedStringSet : SortedSet<string>
    {
        public SortedStringSet()
            : base()
        {

        }

        public SortedStringSet(IEnumerable<string> keys)
            : base(keys)
        {

        }
    }
}
