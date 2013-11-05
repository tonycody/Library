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
    public interface ISettingContent
    {
        Type Type { get; }
        string Name { get; }
        object Value { get; set; }
    }

    public class SettingContent<T> : ISettingContent
    {
        public SettingContent()
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

        object ISettingContent.Value
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
        private Dictionary<string, Content> _dic = new Dictionary<string, Content>();
        private const int _cacheSize = 1024 * 64;

        protected SettingsBase(IEnumerable<ISettingContent> contentList)
        {
            foreach (var content in contentList)
            {
                _dic[content.Name] = new Content()
                {
                    Type = content.Type,
                    Value = content.Value
                };
            }
        }

        protected object this[string propertyName]
        {
            get
            {
                return _dic[propertyName].Value;
            }
            set
            {
                _dic[propertyName].Value = value;
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

                Content content = null;
                if (!_dic.TryGetValue(name, out content)) continue;

                try
                {
                    using (FileStream stream = new FileStream(configPath, FileMode.Open))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                    {
                        using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                        {
                            var ds = new DataContractSerializer(content.Type);
                            content.Value = ds.ReadObject(textDictionaryReader);
                        }
                    }

                    successNames.Add(name);
                }
                catch (Exception e)
                {
                    Log.Warning(e);
                }
            }

            foreach (var configPath in Directory.GetFiles(directoryPath))
            {
                if (!configPath.EndsWith(".gz.bak")) continue;

                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(configPath));
                if (successNames.Contains(name)) continue;

                Content content = null;
                if (!_dic.TryGetValue(name, out content)) continue;

                try
                {
                    using (FileStream stream = new FileStream(configPath, FileMode.Open))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                    {
                        using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                        {
                            var ds = new DataContractSerializer(content.Type);
                            content.Value = ds.ReadObject(textDictionaryReader);
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

            foreach (var item in _dic)
            {
                try
                {
                    var name = item.Key;
                    var type = item.Value.Type;
                    var value = item.Value.Value;

                    string uniquePath = null;

                    using (FileStream stream = SettingsBase.GetUniqueFileStream(Path.Combine(directoryPath, name + ".temp")))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    {
                        uniquePath = stream.Name;

                        using (GZipStream compressStream = new GZipStream(cacheStream, CompressionMode.Compress))
                        using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(compressStream, new UTF8Encoding(false)))
                        {
                            var ds = new DataContractSerializer(type);
                            textDictionaryWriter.WriteStartDocument();
                            ds.WriteObject(textDictionaryWriter, value);
                        }
                    }

                    string newPath = Path.Combine(directoryPath, name + ".gz");
                    string bakPath = Path.Combine(directoryPath, name + ".gz.bak");

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
            return _dic.ContainsKey(propertyName);
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

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
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
                        if (index > 1024) throw;
                    }
                }
            }
        }

        private class Content
        {
            public Type Type { get; set; }
            public object Value { get; set; }
        }
    }
}
