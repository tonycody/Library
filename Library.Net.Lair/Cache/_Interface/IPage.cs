using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IPage : IContent
    {
        string Name { get; }
    }
}
