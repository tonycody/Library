using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMulticastOptions : IComputeHash
    {
        Key Key { get; }
        int Cost { get; }
    }
}
