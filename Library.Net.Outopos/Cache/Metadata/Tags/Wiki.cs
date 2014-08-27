using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Wiki", Namespace = "http://Library/Net/Outopos")]
    public sealed class Wiki : Tag<Wiki>
    {
        public Wiki(string name, byte[] id)
            : base(name, id)
        {

        }
    }
}
