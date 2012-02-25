using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

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
