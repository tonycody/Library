
namespace Library.Net.Lair
{
    interface IChannel : IComputeHash
    {
        byte[] Id { get; }
        string Name { get; }
    }
}
