using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Lair
{
    interface IFilter<TKey, TChannel>
        where TKey : IKey
        where TChannel : IChannel
    {
        TChannel Channel { get; }
        DateTime CreationTime { get; }
        IList<TKey> Keys { get; }
    }
}
