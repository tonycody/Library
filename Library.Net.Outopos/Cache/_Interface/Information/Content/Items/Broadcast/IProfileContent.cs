using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IProfileContent : IComputeHash
    {
        int Cost { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
        IEnumerable<Wiki> Wikis { get; }
        IEnumerable<Chat> Chats { get; }
    }
}
