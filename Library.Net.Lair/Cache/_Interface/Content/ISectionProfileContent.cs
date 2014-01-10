using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfileContent<TWiki, TChat, TExchangePublicKey>
        where TWiki : IWiki
        where TChat : IChat
        where TExchangePublicKey : IExchangeEncrypt
    {
        string Comment { get; }
        TExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleterSignatures { get; }
        IEnumerable<TWiki> Wikis { get; }
        IEnumerable<TChat> Chats { get; }
    }
}
