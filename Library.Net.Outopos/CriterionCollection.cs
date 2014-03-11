using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class CriterionCollection : FilterList<Criterion>, IEnumerable<Criterion>
    {
        public CriterionCollection() : base() { }
        public CriterionCollection(int capacity) : base(capacity) { }
        public CriterionCollection(IEnumerable<Criterion> collections) : base(collections) { }

        protected override bool Filter(Criterion item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<Match>

        IEnumerator<Criterion> IEnumerable<Criterion>.GetEnumerator()
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
