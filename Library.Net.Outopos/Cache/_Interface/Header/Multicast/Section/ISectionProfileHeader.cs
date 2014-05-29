using System;

namespace Library.Net.Outopos
{
    interface ISectionProfileHeader<TSection> : IMulticastHeader<TSection>
        where TSection : ISection
    {

    }
}
