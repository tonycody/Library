using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocument<TArchive, TPage> : IComputeHash
        where TArchive : IArchive
        where TPage : IPage
    {
        TArchive Archive { get; }
        DateTime CreationTime { get; }
        IEnumerable<Page> Pages { get; }
    }
}
