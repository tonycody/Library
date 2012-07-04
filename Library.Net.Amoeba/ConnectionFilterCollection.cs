using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public class ConnectionFilterCollection : LockedList<ConnectionFilter>, IEnumerable<ConnectionFilter>
    {
        public ConnectionFilterCollection() : base() { }
        public ConnectionFilterCollection(int capacity) : base(capacity) { }
        public ConnectionFilterCollection(IEnumerable<ConnectionFilter> collections) : base(collections) { }

        #region IEnumerable<ConnectionFilter>

        IEnumerator<ConnectionFilter> IEnumerable<ConnectionFilter>.GetEnumerator()
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
