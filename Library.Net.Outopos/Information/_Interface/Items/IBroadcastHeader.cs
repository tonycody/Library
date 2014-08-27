using System;

namespace Library.Net.Outopos
{
    public interface IBroadcastHeader : IComputeHash
    {
        DateTime CreationTime { get; }
    }
}
