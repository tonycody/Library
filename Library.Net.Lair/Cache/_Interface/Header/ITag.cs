using System.Collections.Generic;

namespace Library.Net.Lair
{
    public interface ITag
    {
        string Type { get; }
        byte[] Id { get; }
        IEnumerable<string> Arguments { get; }
    }
}
