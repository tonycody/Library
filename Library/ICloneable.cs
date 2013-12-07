
namespace Library
{
    public interface ICloneable<out T>
    {
        T Clone();
    }
}
