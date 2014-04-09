using System.Collections.Generic;

namespace Library.Net.Outopos
{
    public interface ITag
    {
        string Type { get; }
        string Name { get; }
        byte[] Id { get; }
    }
}
