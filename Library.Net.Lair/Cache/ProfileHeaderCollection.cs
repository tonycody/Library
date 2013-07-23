using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ProfileHeaderCollection : FilterList<ProfileHeader>, IEnumerable<ProfileHeader>
    {
        public ProfileHeaderCollection() : base() { }
        public ProfileHeaderCollection(int capacity) : base(capacity) { }
        public ProfileHeaderCollection(IEnumerable<ProfileHeader> collections) : base(collections) { }

        protected override bool Filter(ProfileHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ProfileHeader>

        IEnumerator<ProfileHeader> IEnumerable<ProfileHeader>.GetEnumerator()
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
