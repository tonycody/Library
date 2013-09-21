using Library.Security;

namespace Library.Net.Amoeba
{
    interface ILink
    {
        SignatureCollection TrustSignatures { get; }
    }
}
