using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ProfileCollection : FilterList<Profile>, IEnumerable<Profile>
    {
        public ProfileCollection() : base() { }
        public ProfileCollection(int capacity) : base(capacity) { }
        public ProfileCollection(IEnumerable<Profile> collections) : base(collections) { }

        protected override bool Filter(Profile item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Profile>

        IEnumerator<Profile> IEnumerable<Profile>.GetEnumerator()
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
