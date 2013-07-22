using System;

namespace Library.Net.Lair
{
    interface ITopicContent
    {
        ContentFormatType FormatType { get; }
        string Content { get; }
    }
}
