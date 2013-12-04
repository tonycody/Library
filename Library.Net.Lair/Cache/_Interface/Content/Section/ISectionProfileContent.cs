using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
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
