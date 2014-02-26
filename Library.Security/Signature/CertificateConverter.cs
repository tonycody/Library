using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Library.Security
{
    public static class CertificateConverter
    {
        public static Stream ToCertificateStream(Certificate item)
        {
            return Converter.ToCertificateStream(item);
        }

        public static Certificate FromCertificateStream(Stream stream)
        {
            return Converter.FromCertificateStream(stream);
        }
    }
}
