using System.Collections.Generic;

namespace Library.Net.Lair
{
    public interface ITag
    {
        string Type { get; }
        byte[] Id { get; }
        string Name { get; }
    }
}
