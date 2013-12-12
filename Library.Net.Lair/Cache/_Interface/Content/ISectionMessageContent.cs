
namespace Library.Net.Lair
{
    interface ISectionMessageContent<TKey>
        where TKey : IKey
    {
        string Comment { get; }
        TKey Anchor { get; }
    }
}
