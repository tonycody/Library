using System;

namespace Library.Net.Lair
{
    interface ISectionMessageHeader<TSection> : IHeader<TSection>
        where TSection : ISection
    {

    }
}
