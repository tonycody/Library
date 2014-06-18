using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IBroadcastHeader : IComputeHash
    {
        DateTime CreationTime { get; }
        int Coin { get; }
        Key Key { get; }
    }
}
