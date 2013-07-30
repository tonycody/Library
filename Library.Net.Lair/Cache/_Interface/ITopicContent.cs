using System;

namespace Library.Net.Lair
{
    interface ITopicContent : IContentFormatType
    {
        string Content { get; }
    }
}
