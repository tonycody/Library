using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface ISignatureMessage : IComputeHash
    {
        string Signature { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
    }
}
