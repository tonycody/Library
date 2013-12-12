using System;

namespace Library.Net.Lair
{
    interface IArchiveVoteHeader<TArchive> : IHeader<TArchive>
        where TArchive : IArchive
    {

    }
}
