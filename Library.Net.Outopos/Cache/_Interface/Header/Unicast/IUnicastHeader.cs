using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IUnicastHeader : IComputeHash
    {
        string Signature { get; }
        DateTime CreationTime { get; }
        int Coin { get; }
        Key Key { get; }
    }
}
