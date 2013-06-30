using System;

namespace Library.Net.Amoeba
{
    interface IBox
    {
        string Name { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        SeedCollection Seeds { get; }
        BoxCollection Boxes { get; }
    }
}
