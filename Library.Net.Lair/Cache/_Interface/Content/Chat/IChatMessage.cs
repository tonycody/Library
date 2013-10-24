using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IChatMessage<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
