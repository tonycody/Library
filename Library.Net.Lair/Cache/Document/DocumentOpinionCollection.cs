using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class DocumentOpinionCollection : FilterList<DocumentOpinion>, IEnumerable<DocumentOpinion>
    {
        public DocumentOpinionCollection() : base() { }
        public DocumentOpinionCollection(int capacity) : base(capacity) { }
        public DocumentOpinionCollection(IEnumerable<DocumentOpinion> collections) : base(collections) { }

        protected override bool Filter(DocumentOpinion item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<DocumentOpinion>

        IEnumerator<DocumentOpinion> IEnumerable<DocumentOpinion>.GetEnumerator()
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
