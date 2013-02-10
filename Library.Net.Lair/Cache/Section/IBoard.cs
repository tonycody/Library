using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface IBoard<TChannel>
        where TChannel : IChannel
    {
        TChannel Channel { get; }
        string Content { get; }
    }
}
