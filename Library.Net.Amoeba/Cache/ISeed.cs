using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Amoeba
{
    interface ISeed<TKey, TKeyword> : IKeywords<TKeyword>, ICompressionAlgorithm, ICryptoAlgorithm
        where TKey : IKey
        where TKeyword : IKeyword
    {
        string Name { get; }
        DateTime CreationTime { get; }
        long Length { get; }
        string Comment { get; }
        int Rank { get; }
        TKey Key { get; }
    }
}
