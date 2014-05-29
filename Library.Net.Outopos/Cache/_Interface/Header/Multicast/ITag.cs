using System.Collections.Generic;

namespace Library.Net.Outopos
{
    public interface ITag
    {
        string Name { get; }
        byte[] Id { get; }
    }
}
