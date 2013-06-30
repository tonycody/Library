using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IMail<TSection> : IComputeHash
        where TSection : ISection
    {
        TSection Section { get; }
        string RecipientSignature { get; }
        DateTime CreationTime { get; }
        byte[] Content { get; }
    }
}
