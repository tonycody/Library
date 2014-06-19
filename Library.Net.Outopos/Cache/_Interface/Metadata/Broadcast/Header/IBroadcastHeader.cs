using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IBroadcastHeader : IComputeHash
    {
        DateTime CreationTime { get; }
        Key Key { get; }
        int Coin { get; }
    }
}
