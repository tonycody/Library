using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentArchive<TDocumentPage>
        where TDocumentPage : IDocumentPage
    {
        IEnumerable<TDocumentPage> Pages { get; }
    }
}
