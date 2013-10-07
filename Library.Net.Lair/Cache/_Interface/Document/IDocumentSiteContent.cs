using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentSiteContent<TDocumentPage>
        where TDocumentPage : IDocumentPage
    {
        IEnumerable<TDocumentPage> DocumentPages { get; }
    }
}
