using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class OptionCollection : FilterList<string>, IEnumerable<string>
    {
        public OptionCollection() : base() { }
        public OptionCollection(int capacity) : base(capacity) { }
        public OptionCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxOptionLength = 1024;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > OptionCollection.MaxOptionLength) return true;

            return false;
        }
    }
}
