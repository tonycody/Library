using System;

namespace Library.Net.Lair
{
    interface ICryptoContent : ICryptoAlgorithm
    {
        ArraySegment<byte> Content { get; }
    }
}
