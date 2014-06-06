using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMetadata<TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}
