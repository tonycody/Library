using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IChatTopic : IComputeHash
    {
        Chat Tag { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
    }
}
