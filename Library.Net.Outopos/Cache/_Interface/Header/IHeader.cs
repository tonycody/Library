using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IHeader<TMetadata, TTag> : IComputeHash
        where TMetadata : IMetadata<TTag>
        where TTag : ITag
    {
        TMetadata Metadata { get; }
    }
}
