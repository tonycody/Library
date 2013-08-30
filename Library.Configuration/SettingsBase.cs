using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Io;

namespace Library.Configuration
{
    public interface ISettingsContext
    {
        Type Type { get; }
        string Name { get; }
        object Value { get; set; }
    }

    public class SettingsContext<T> : ISettingsContext
    {
        public SettingsContext()
        {

        }

        public T Value { get; set; }

        #region IContext

        public Type Type
        {
            get
            {
                return typeof(T);
            }
        }

        public string Name { get; set; }

        object ISettingsContext.Value
        {
            get
            {
                return this.Value;
            }
            set
            {
                this.Value = (T)value;
            }
        }

        #endregion
    }

    public abstract class SettingsBase : ISettings
    {
        private IList<ISettingsContext> _contextList;
        private const int _cacheSize = 1024 * 64;

        protected SettingsBase(IEnumerable<ISettingsContext> contextList)
        {
            _contextList = contextList.ToList();
        }

        protected object this[string propertyName]
        {
            get
            {
                ISettingsContext t = _contextList.First(n => n.Name == propertyName);

                return t.Value;
            }
            set
            {
                ISettingsContext t = _contextList.First(n => n.Name == propertyName);

                t.Value = value;
            }
        }

        #region ISettings

        public virtual void Load(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            HashSet<string> successNames = new HashSet<string>();

            foreach (var configPath in Directory.GetFiles(directoryPath))
            {
                if (!configPath.EndsWith(".gz")) continue;

                var name = Path.GetFileNameWithoutExtension(configPath);

                var context = _contextList.FirstOrDefault(n => n.Name == name);
                if (context == null) continue;

                try
                {
                    using (FileStream stream = new FileStream(configPath, FileMode.Open))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                    {
                        using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                        {
                            var ds = new DataContractSerializer(context.Type);
                            context.Value = ds.ReadObject(textDictionaryReader);
                        }
                    }

                    successNames.Add(context.Name);
                }
                catch (Exception)
                {

                }
            }

            foreach (var configPath in Directory.GetFiles(directoryPath))
            {
                if (!configPath.EndsWith(".gz.bak")) continue;

                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(configPath));
                if (successNames.Contains(name)) continue;

                var context = _contextList.FirstOrDefault(n => n.Name == name);
                if (context == null) continue;

                try
                {
                    using (FileStream stream = new FileStream(configPath, FileMode.Open))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                    {
                        using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                        {
                            var ds = new DataContractSerializer(context.Type);
                            context.Value = ds.ReadObject(textDictionaryReader);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(e);
                }
            }
        }

        public virtual void Save(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            foreach (var value in _contextList)
            {
                try
                {
                    string uniquePath = null;

                    using (FileStream stream = SettingsBase.GetUniqueFileStream(Path.Combine(directoryPath, value.Name + ".temp")))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    {
                        uniquePath = stream.Name;

                        using (GZipStream compressStream = new GZipStream(cacheStream, CompressionMode.Compress))
                        using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(compressStream, new UTF8Encoding(false)))
                        {
                            var ds = new DataContractSerializer(value.Type);
                            textDictionaryWriter.WriteStartDocument();
                            ds.WriteObject(textDictionaryWriter, value.Value);
                        }
                    }

                    string newPath = Path.Combine(directoryPath, value.Name + ".gz");
                    string bakPath = Path.Combine(directoryPath, value.Name + ".gz.bak");

                    if (File.Exists(newPath))
                    {
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }

                        File.Move(newPath, bakPath);
                    }

                    File.Move(uniquePath, newPath);
                }
                catch (Exception e) 
                {
                    Log.Warning(e);
                }
            }
        }

        #endregion

        protected bool Contains(string propertyName)
        {
            return _contextList.Any(n => n.Name == propertyName);
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    FileStream fs = new FileStream(path, FileMode.CreateNew);
                    return fs;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    throw;
                }
            }

            for (int index = 1, count = 0; ; index++)
            {
                string text = string.Format(
                    @"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        FileStream fs = new FileStream(text, FileMode.CreateNew);
                        return fs;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        count++;
                        if (count > 1024)
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }
}
