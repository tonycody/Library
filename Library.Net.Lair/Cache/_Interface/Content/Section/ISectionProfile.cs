using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfile<TTag> : IExchangeEncrypt
        where TTag : ITag
    {
        string Comment { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<TTag> Links { get; }
    }
}
