using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IArchiveDocumentContent<TPage>
        where TPage : IPage
    {
        IEnumerable<TPage> Pages { get; }
    }
}
