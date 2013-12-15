using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    public interface IGroup<TKey> : ICorrectionAlgorithm
          where TKey : IKey
    {
        IList<TKey> Keys { get; }
    }
}
