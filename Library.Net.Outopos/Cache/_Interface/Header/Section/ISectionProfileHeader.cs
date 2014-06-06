using System;

namespace Library.Net.Outopos
{
    interface ISectionProfileHeader<TMetadata, TSection> : IHeader<TMetadata, TSection>
        where TMetadata : IMetadata<TSection>
        where TSection : ISection
    {

    }
}
