using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IWhisperMessageContent<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
