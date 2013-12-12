using System;

namespace Library.Net.Lair
{
    interface ISectionProfileHeader<TSection> : IHeader<TSection>
        where TSection : ISection
    {

    }
}
