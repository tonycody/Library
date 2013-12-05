using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IDocumentVoteContent<TKey>
        where TKey : IKey
    {
        IEnumerable<TKey> Goods { get; }
        IEnumerable<TKey> Bads { get; }
    }
}
