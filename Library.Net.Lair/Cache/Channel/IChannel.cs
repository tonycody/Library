﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface IChannel
    {
        byte[] Id { get; }
        string Name { get; }
    }
}