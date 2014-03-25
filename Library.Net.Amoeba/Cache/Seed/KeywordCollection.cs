using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class KeywordCollection : LockedList<string>
    {
        public KeywordCollection() : base() { }
        public KeywordCollection(int capacity) : base(capacity) { }
        public KeywordCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxKeywordLength = 256;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > KeywordCollection.MaxKeywordLength) return true;

            return false;
        }
    }
}
