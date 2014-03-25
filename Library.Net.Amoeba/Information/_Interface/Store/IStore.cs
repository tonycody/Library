
using System.Collections.Generic;
namespace Library.Net.Amoeba
{
    public interface IStore
    {
        ICollection<Box> Boxes { get; }
    }
}
