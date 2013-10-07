using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class SignatureProfileCollection : FilterList<SignatureProfile>, IEnumerable<SignatureProfile>
    {
        public SignatureProfileCollection() : base() { }
        public SignatureProfileCollection(int capacity) : base(capacity) { }
        public SignatureProfileCollection(IEnumerable<SignatureProfile> collections) : base(collections) { }

        protected override bool Filter(SignatureProfile item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<SignatureProfile>

        IEnumerator<SignatureProfile> IEnumerable<SignatureProfile>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion
    }
}
