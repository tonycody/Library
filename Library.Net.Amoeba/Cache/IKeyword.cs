using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IKeyword : IHashAlgorithm
    {
        string Value { get; }
        byte[] Hash { get; }
    }
}
