using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfileContent<TLink, TTag>
        where TLink : ILink<Tag>
        where TTag : ITag
    {
        string Comment { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TLink> Links { get; }
    }
}
