using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IIndex<TGroup, TKey> : ICompressionAlgorithm, ICryptoAlgorithm
        where TKey : IKey
        where TGroup : IGroup<TKey>
    {
        IList<TGroup> Groups { get; }
    }
}
