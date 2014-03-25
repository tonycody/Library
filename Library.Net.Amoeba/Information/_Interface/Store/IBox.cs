using System;
using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    public interface IBox
    {
        string Name { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        ICollection<Seed> Seeds { get; }
        ICollection<Box> Boxes { get; }
    }
}
