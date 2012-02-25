using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IGroup<TKey> : ICorrectionAlgorithm
        where TKey : IKey
    {
        IList<TKey> Keys { get; }
    }
}
