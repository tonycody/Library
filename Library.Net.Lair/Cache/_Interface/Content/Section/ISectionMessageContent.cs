using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface ISectionMessageContent<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        TKey Anchor { get; }
    }
}
