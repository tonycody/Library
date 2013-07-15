using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IMail<TSection> : IComputeHash
        where TSection : ISection
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string RecipientSignature { get; }
        byte[] Content { get; }
    }
}
