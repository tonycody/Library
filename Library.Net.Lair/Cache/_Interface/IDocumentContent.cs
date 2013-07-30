using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentContent<TPage>
          where TPage : IPage
    {
        IEnumerable<Page> Pages { get; }
    }
}
