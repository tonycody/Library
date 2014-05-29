using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Outopos
{
    interface ISetOperators<T>
    {
        IEnumerable<T> IntersectFrom(IEnumerable<T> collection);
        IEnumerable<T> ExceptFrom(IEnumerable<T> collection);
    }
}
