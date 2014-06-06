using System;

namespace Library.Net.Outopos
{
    interface ISectionMessageHeader<TMetadata, TSection> : IHeader<TMetadata, TSection>
        where TMetadata : IMetadata<TSection>
        where TSection : ISection
    {

    }
}
