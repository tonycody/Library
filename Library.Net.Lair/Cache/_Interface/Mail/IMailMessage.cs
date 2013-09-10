using System;

namespace Library.Net.Lair
{
    interface IMailMessage<TMail, TKey> : IComputeHash
        where TMail : IMail
        where TKey : IKey
    {
        TMail Mail { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
