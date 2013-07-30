using System;

namespace Library.Net.Lair
{
    interface IMessage<TChannel, TKey> : IComputeHash
        where TChannel : IChannel
        where TKey : IKey
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
