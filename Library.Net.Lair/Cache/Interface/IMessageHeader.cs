using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IMessageHeader<TChannel, TKey> : IComputeHash
        where TChannel : IChannel
        where TKey : IKey
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
