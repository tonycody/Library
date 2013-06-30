
namespace Library.Net.Lair
{
    interface ISection : IComputeHash
    {
        byte[] Id { get; }
        string Name { get; }
    }
}
