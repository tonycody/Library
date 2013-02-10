using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface ICreator<TSection, TBoard, TChannel> : IComputeHash
        where TSection : ISection
        where TBoard : IBoard<TChannel>
        where TChannel : IChannel
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<Board> Boards { get; }
        IEnumerable<string> FilterSignatures { get; }
    }
}
