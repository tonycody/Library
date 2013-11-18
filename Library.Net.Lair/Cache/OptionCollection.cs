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

        #region IEnumerable<Option>

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
