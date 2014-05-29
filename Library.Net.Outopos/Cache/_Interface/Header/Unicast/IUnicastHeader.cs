using System;

namespace Library.Net.Outopos
{
    public interface IUnicastHeader : IComputeHash
    {
        string Signature { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
        byte[] Option { get; }
    }
}
