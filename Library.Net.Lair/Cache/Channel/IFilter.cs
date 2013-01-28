using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface IFilter<TChannel, TKey> : IComputeHash
        where TChannel : IChannel
        where TKey : IKey
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
