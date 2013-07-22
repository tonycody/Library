using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentHeader<TSection, TKey> : IComputeHash
        where TSection : ISection
        where TKey : IKey
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
