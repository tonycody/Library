using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    interface IProfileContent<TChannel> : IExchangeEncrypt
        where TChannel : IChannel
    {
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TChannel> Channels { get; }
    }
}
