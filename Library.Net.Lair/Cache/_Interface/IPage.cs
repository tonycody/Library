using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IPage : IContentFormatType
    {
        string Name { get; }
        DateTime CreationTime { get; }
        string Content { get; }
    }
}
