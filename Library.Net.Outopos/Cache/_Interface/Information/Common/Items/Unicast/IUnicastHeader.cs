using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IUnicastHeader
    {
        string Signature { get; }
        DateTime CreationTime { get; }
    }
}
