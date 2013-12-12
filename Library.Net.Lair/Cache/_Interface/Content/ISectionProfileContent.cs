using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfileContent<TDocument, TChat>
        where TDocument : IDocument
        where TChat : IChat
    {
        string Comment { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TDocument> Documents { get; }
        IEnumerable<TChat> Chats { get; }
    }
}
