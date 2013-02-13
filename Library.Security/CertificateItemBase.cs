using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Library.Security
{
    [DataContract(Name = "CertificateItemBase", Namespace = "http://Library/Security")]
    public abstract class CertificateItemBase<T> : ItemBase<T>, ICertificate
        where T : CertificateItemBase<T>
    {
        public void CreateCertificate(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null)
            {
                this.Certificate = null;
            }
            else
            {
                using (var stream = this.GetCertificateStream())
                {
                    this.Certificate = new Certificate(digitalSignature, stream);
                }
            }
        }

        public bool VerifyCertificate()
        {
            if (this.Certificate == null)
            {
                return true;
            }
            else
            {
                using (var stream = this.GetCertificateStream())
                {
                    return this.Certificate.Verify(stream);
                }
            }
        }

        protected abstract Stream GetCertificateStream();

        [DataMember(Name = "Certificate")]
        public abstract Certificate Certificate { get; protected set; }
    }
}
