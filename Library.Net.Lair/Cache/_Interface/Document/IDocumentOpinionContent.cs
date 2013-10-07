using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IDocumentOpinionContent
    {
        IEnumerable<string> Goods { get; }
        IEnumerable<string> Bads { get; }
    }
}
