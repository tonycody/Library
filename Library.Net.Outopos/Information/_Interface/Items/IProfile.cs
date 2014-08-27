using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IProfile : IComputeHash
    {
        DateTime CreationTime { get; }
        int Cost { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<Wiki> Wikis { get; }
        IEnumerable<Chat> Chats { get; }
    }
}
