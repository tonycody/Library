using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IProfile : IComputeHash
    {
        DateTime CreationTime { get; }
        int Cost { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        SignatureCollection TrustSignatures { get; }
        WikiCollection Wikis { get; }
        ChatCollection Chats { get; }
    }
}
