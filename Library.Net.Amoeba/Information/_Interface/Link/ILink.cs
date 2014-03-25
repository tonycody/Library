using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Amoeba
{
    public interface ILink
    {
        ICollection<string> TrustSignatures { get; }
    }
}
