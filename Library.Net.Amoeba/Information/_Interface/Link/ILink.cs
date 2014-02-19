using Library.Security;

namespace Library.Net.Amoeba
{
    public interface ILink
    {
        SignatureCollection TrustSignatures { get; }
    }
}
