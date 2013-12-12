using System;

namespace Library.Net.Lair
{
    interface IArchiveDocumentHeader<TArchive> : IHeader<TArchive>
        where TArchive : IArchive
    {

    }
}
