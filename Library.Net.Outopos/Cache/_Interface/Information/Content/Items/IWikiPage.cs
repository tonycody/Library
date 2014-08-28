﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Outopos
{
    public interface IWikiPage : IHypertext
    {
        string Path { get; }
        DateTime CreationTime { get; }
    }
}
