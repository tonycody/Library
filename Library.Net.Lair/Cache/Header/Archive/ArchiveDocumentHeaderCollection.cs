﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    internal sealed class ArchiveDocumentHeaderCollection : FilterList<ArchiveDocumentHeader>, IEnumerable<ArchiveDocumentHeader>
    {
        public ArchiveDocumentHeaderCollection() : base() { }
        public ArchiveDocumentHeaderCollection(int capacity) : base(capacity) { }
        public ArchiveDocumentHeaderCollection(IEnumerable<ArchiveDocumentHeader> collections) : base(collections) { }

        protected override bool Filter(ArchiveDocumentHeader item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<ArchiveDocumentHeader>

        IEnumerator<ArchiveDocumentHeader> IEnumerable<ArchiveDocumentHeader>.GetEnumerator()
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