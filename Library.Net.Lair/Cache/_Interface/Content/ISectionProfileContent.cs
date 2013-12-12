using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfileContent<TArchive, TChat>
        where TArchive : IArchive
        where TChat : IChat
    {
        string Comment { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TArchive> Archives { get; }
        IEnumerable<TChat> Chats { get; }
    }
}
