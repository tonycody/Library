using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IVoteContent<TKey>
        where TKey : IKey
    {
        IEnumerable<TKey> Goods { get; }
        IEnumerable<TKey> Bads { get; }
    }
}
