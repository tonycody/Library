using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IChatMessageContent<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
