using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface ICreator<TSection> : IComputeHash
        where TSection : ISection
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<Channel> Channels { get; }
        IEnumerable<string> FilterSignatures { get; }
    }
}
