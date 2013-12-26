
namespace Library.Net.Lair
{
    interface ISectionMessageContent<TAnchor>
        where TAnchor : IAnchor
    {
        string Comment { get; }
        TAnchor Anchor { get; }
    }
}
