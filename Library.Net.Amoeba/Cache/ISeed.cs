using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Amoeba
{
    interface ISeed<TKey> : IKeywords, ICompressionAlgorithm, ICryptoAlgorithm
        where TKey : IKey
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        int Rank { get; }
        TKey Key { get; }
    }
}
