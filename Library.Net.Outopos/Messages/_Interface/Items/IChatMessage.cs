using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IChatMessage : IComputeHash
    {
        Chat Tag { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        private AnchorCollection Anchors { get; }
    }
}
