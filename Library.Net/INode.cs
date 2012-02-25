namespace Library.Net
{
    /// <summary>
    /// ノードに関する情報を表します
    /// </summary>
    public interface INode
    {
        /// <summary>
        /// ノードIDを取得します
        /// </summary>
        byte[] Id { get; }
    }
}
