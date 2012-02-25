namespace Library.Configuration
{
    public interface ISettings
    {
        void Load(string directoryPath);
        void Save(string directoryPath);
    }
}
