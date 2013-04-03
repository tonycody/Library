using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CertificateItemBase", Namespace = "http://Library/Security")]
    public abstract class ReadOnlyCertificateItemBase<T> : ItemBase<T>, ICertificate
        where T : ReadOnlyCertificateItemBase<T>
    {
        protected void CreateCertificate(DigitalSignature digitalSignature)
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
