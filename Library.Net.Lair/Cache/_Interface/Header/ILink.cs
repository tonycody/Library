
namespace Library.Net.Lair
{
    interface ILink<TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        string Type { get; }
        string Path { get; }
    }
}
