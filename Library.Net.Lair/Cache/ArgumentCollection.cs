using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class ArgumentCollection : FilterList<string>, IEnumerable<string>
    {
        public ArgumentCollection() : base() { }
        public ArgumentCollection(int capacity) : base(capacity) { }
        public ArgumentCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxArgumentLength = 1024;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > ArgumentCollection.MaxArgumentLength) return true;

            return false;
        }

        #region IEnumerable<Argument>

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
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
