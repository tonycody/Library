using System.Collections.Generic;

namespace Library.Net.Lair
{
    public interface ITag
    {
        byte[] Id { get; }
        string Name { get; }
    }
}
