using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CompressionAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CompressionAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Lzma")]
        Lzma = 1,

        [EnumMember(Value = "Xz")]
        Xz = 2,
    }

    public interface ICompressionAlgorithm
    {
        CompressionAlgorithm CompressionAlgorithm { get; }
    }
}
