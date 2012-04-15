using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class KeywordCollection : LockedList<Keyword>, IEnumerable<Keyword>
    {
        public KeywordCollection() : base() { }
        public KeywordCollection(int capacity) : base(capacity) { }
        public KeywordCollection(IEnumerable<Keyword> collections) : base(collections) { }

        #region IEnumerable<Keyword> メンバ

        IEnumerator<Keyword> IEnumerable<Keyword>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable メンバ

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
