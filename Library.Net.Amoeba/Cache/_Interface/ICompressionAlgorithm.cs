using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CompressionAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CompressionAlgorithm
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Lzma")]
        Lzma = 1,
    }

    public interface ICompressionAlgorithm
    {
        CompressionAlgorithm CompressionAlgorithm { get; }
    }
}
