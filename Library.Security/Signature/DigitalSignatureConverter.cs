using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Library.Security
{
    public static class DigitalSignatureConverter
    {
        public static Stream ToDigitalSignatureStream(DigitalSignature item)
        {
            return Converter.ToDigitalSignatureStream(item);
        }

        public static DigitalSignature FromDigitalSignatureStream(Stream stream)
        {
            return Converter.FromDigitalSignatureStream(stream);
        }
    }
}
