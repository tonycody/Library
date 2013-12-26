using System.Collections.Generic;
using Library.Collections;

namespace Library.Security
{
    public sealed class SignatureCollection : FilterList<string>, IEnumerable<string>
    {
        public SignatureCollection() : base() { }
        public SignatureCollection(int capacity) : base(capacity) { }
        public SignatureCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            if (item == null || !Signature.HasSignature(item)) return true;

            return false;
        }
    }
}
