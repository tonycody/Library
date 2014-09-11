using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IBroadcastOptions : IComputeHash
    {
        Key Key { get; }
    }
}
