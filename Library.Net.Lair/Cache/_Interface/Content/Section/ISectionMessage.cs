using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface ISectionMessage<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        TKey Anchor { get; }
    }
}
