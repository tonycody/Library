using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Lair")]
    public enum CryptoAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0,
    }

    interface IProfile<TSection, TChannel, TArchive> : IComputeHash
        where TSection : ISection
        where TChannel : IChannel
        where TArchive : IArchive
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        IEnumerable<string> TrustSignatures { get; }
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
        IEnumerable<TChannel> Channels { get; }
        IEnumerable<TArchive> Archives { get; }
    }
}
