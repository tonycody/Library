using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class UriCollection : LockedList<string>, IEnumerable<string>
    {
        public UriCollection() : base() { }
        public UriCollection(int capacity) : base(capacity) { }
        public UriCollection(IEnumerable<string> collections) : base(collections) { }

        #region IEnumerable<string> メンバ

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
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
