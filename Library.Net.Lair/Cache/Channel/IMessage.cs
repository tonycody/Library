using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface IMessage<TChannel, TKey> : IComputeHash
        where TChannel : IChannel
        where TKey : IKey
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        string Content { get; }
        IEnumerable<TKey> Anchors { get; }
    }
}
