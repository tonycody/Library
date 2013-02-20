using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface ITopic<TChannel> : IComputeHash
        where TChannel : IChannel
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        string Content { get; }
    }
}
