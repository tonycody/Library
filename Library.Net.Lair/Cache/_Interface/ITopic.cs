using System;

namespace Library.Net.Lair
{
    interface ITopic<TChannel> : IContent, IComputeHash
        where TChannel : IChannel
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
    }
}
