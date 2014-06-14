using System;

namespace Library.Net.Outopos
{
    interface IMailMessageHeader<TMail> : IHeader<TMail>
        where TMail : IMail
    {

    }
}
