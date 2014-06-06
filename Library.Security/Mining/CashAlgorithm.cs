using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashAlgorithm", Namespace = "http://Library/Security")]
    public enum CashAlgorithm : byte
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0,
    }
}
