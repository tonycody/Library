using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IKeywords<TKeyword>
        where TKeyword : IKeyword
    {
        IList<TKeyword> Keywords { get; }
    }
}
