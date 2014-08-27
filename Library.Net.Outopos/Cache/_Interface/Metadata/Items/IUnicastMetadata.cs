using System;

namespace Library.Net.Outopos
{
    public interface IUnicastMetadata : IComputeHash
    {
        string Signature { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
        int Cost { get; }
    }
}
