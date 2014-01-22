using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CorrectionAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CorrectionAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "ReedSolomon8")]
        ReedSolomon8 = 1,
    }

    public interface ICorrectionAlgorithm
    {
        CorrectionAlgorithm CorrectionAlgorithm { get; }
        int InformationLength { get; }
        int BlockLength { get; }
        long Length { get; }
    }
}
