using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface IIndex<TGroup, TKey> : ICompressionAlgorithm, ICryptoAlgorithm
        where TGroup : IGroup<TKey>
        where TKey : IKey
    {
        IList<TGroup> Groups { get; }
    }
}
