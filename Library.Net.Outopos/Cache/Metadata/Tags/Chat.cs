using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Chat", Namespace = "http://Library/Net/Outopos")]
    public sealed class Chat : Tag<Chat>
    {
        public Chat(string name, byte[] id)
            : base(name, id)
        {

        }
    }
}
