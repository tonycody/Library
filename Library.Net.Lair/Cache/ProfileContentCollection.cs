using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ProfileContentCollection : FilterList<ProfileContent>, IEnumerable<ProfileContent>
    {
        public ProfileContentCollection() : base() { }
        public ProfileContentCollection(int capacity) : base(capacity) { }
        public ProfileContentCollection(IEnumerable<ProfileContent> collections) : base(collections) { }

        protected override bool Filter(ProfileContent item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ProfileContent>

        IEnumerator<ProfileContent> IEnumerable<ProfileContent>.GetEnumerator()
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
