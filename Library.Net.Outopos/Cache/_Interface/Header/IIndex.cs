using System.Collections.Generic;

namespace Library.Net.Outopos
{
    public interface IIndex<TKey>
      where TKey : IKey
    {
        ICollection<TKey> Keys { get; }
    }
}
