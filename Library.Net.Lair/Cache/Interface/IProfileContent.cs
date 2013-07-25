using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IProfileContent<TChannel> : IExchangeAlgorithm
        where TChannel : IChannel
    {
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TChannel> Channels { get; }
    }
}
