using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentOpinionsContent<TKey>
        where TKey : IKey
    {
        IEnumerable<TKey> Goods { get; }
        IEnumerable<TKey> Bads { get; }
    }
}
