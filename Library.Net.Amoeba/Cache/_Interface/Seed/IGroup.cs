using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface IGroup<TKey> : ICorrectionAlgorithm
        where TKey : IKey
    {
        IList<TKey> Keys { get; }
    }
}
