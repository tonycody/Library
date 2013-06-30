using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IArchive : IComputeHash
    {
        byte[] Id { get; }
        string Name { get; }
    }
}
