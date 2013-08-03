using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IMessageContent<TKey>
        where TKey : IKey
    {
        string Text { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
