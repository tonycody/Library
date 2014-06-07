using System;

namespace Library.Net.Outopos
{
    interface ISectionMessageHeader<TSection> : IHeader<TSection>
        where TSection : ISection
    {

    }
}
