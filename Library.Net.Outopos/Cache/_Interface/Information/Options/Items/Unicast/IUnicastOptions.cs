using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IUnicastOptions : IComputeHash
    {
        Key Key { get; }
        int Cost { get; }
    }
}
