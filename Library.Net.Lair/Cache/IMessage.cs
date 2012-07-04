using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Lair
{
    interface IMessage<TChannel>
        where TChannel : IChannel
    {
        TChannel Channel { get; }
        string Title { get; }
        DateTime CreationTime { get; }
        string Content { get; }
    }
}
