using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IProfile<TSection, TChannel, TArchive> : ICryptoAlgorithm, IComputeHash
        where TSection : ISection
        where TChannel : IChannel
        where TArchive : IArchive
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TChannel> Channels { get; }
        IEnumerable<TArchive> Archives { get; }
    }
}
