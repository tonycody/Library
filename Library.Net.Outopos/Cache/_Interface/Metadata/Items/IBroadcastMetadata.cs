using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IBroadcastMetadata : IComputeHash
    {
        DateTime CreationTime { get; }
        Key Key { get; }
        int Cost { get; }
    }
}
