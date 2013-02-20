using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface ICreator<TSection, TChannel> : IComputeHash
        where TSection : ISection
        where TChannel : IChannel
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<Channel> Channels { get; }
    }
}
