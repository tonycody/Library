using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
