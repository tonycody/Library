using System;

namespace Library.Net.Outopos
{
    public interface IBroadcastMetadata : IComputeHash
    {
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}
