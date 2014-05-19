using System.Collections.Generic;

namespace Library.Net.Outopos
{
    interface IIndex<TKey>
      where TKey : IKey
    {
        IEnumerable<TKey> Keys { get; }
    }
}
