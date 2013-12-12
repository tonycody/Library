using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentArchiveContent<TPage>
        where TPage : IPage
    {
        IEnumerable<TPage> Pages { get; }
    }
}
