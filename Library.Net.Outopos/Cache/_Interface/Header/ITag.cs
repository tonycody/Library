using System.Collections.Generic;

namespace Library.Net.Outopos
{
    public interface ITag
    {
        string Type { get; }
        byte[] Id { get; }
    }
}
