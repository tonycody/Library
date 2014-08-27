using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IWikiPage : IHypertext, IComputeHash
    {
        Wiki Tag { get; }
        DateTime CreationTime { get; }
    }
}
