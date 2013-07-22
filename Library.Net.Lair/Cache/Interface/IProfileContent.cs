using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IProfileContent<TChannel, TArchive> : ICryptoAlgorithm
        where TChannel : IChannel
        where TArchive : IArchive
    {
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TChannel> Channels { get; }
        IEnumerable<TArchive> Archives { get; }
    }
}
