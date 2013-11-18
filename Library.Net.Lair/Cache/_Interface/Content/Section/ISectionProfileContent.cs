using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    interface ISectionProfileContent : IExchangeEncrypt
    {
        string Comment { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> Links { get; }
    }
}
