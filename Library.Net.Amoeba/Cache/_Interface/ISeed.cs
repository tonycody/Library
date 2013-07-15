using System;
using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface ISeed<TKey> : ICompressionAlgorithm, ICryptoAlgorithm
        where TKey : IKey
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        IList<string> Keywords { get; }
        string Comment { get; }
        int Rank { get; }
        TKey Key { get; }
    }
}
