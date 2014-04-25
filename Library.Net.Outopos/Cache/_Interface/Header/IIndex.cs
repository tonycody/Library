using System.Collections.Generic;

namespace Library.Net.Outopos
{
    public interface IIndex<TKey>
      where TKey : IKey
    {
        IEnumerable<TKey> Keys { get; }
    }
}
