using System;

namespace Library.Net.Lair
{
    interface ITopicHeader<TChannel, TKey> : IComputeHash
        where TChannel : IChannel
        where TKey : IKey
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
