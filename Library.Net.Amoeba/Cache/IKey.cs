using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
