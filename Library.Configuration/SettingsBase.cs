using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Io;
using System.Diagnostics;

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
        private const int _cacheSize = 1024 * 32;

        protected SettingsBase(IEnumerable<ISettingContent> contents)
        {
            foreach (var content in contents)
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
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            HashSet<string> successNames = new HashSet<string>();

            // DataContractSerializerのBinaryバージョン
            foreach (var extension in new string[] { ".v2", ".v2.bak" })
            {
                foreach (var configPath in Directory.GetFiles(directoryPath))
                {
                    if (!configPath.EndsWith(extension)) continue;

                    var name = Path.GetFileName(configPath.Substring(0, configPath.Length - extension.Length));
                    if (successNames.Contains(name)) continue;

                    Content content = null;
                    if (!_dic.TryGetValue(name, out content)) continue;

                    try
                    {
                        using (FileStream stream = new FileStream(configPath, FileMode.Open))
                        using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                        using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                        {
                            //using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                            using (XmlDictionaryReader xml = XmlDictionaryReader.CreateBinaryReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                            {
                                var deserializer = new DataContractSerializer(content.Type);
                                content.Value = deserializer.ReadObject(xml);
                            }
                        }

                        successNames.Add(name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                }
            }

            // DataContractSerializerのTextバージョン
            // 互換性は高いが処理速度が遅い。
            foreach (var extension in new string[] { ".gz", ".gz.bak" })
            {
                foreach (var configPath in Directory.GetFiles(directoryPath))
                {
                    if (!configPath.EndsWith(extension)) continue;

                    var name = Path.GetFileName(configPath.Substring(0, configPath.Length - extension.Length));
                    if (successNames.Contains(name)) continue;

                    Content content = null;
                    if (!_dic.TryGetValue(name, out content)) continue;

                    try
                    {
                        using (FileStream stream = new FileStream(configPath, FileMode.Open))
                        using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                        using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                        {
                            using (XmlDictionaryReader xml = XmlDictionaryReader.CreateTextReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                            {
                                var deserializer = new DataContractSerializer(content.Type);
                                content.Value = deserializer.ReadObject(xml);
                            }
                        }

                        successNames.Add(name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                }
            }

            sw.Stop();
            Debug.WriteLine("Settings Load {0} {1}", Path.GetFileName(directoryPath), sw.ElapsedMilliseconds);
        }

        public virtual void Save(string directoryPath)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

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
                        //using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(compressStream, new UTF8Encoding(false)))
                        using (XmlDictionaryWriter xml = XmlDictionaryWriter.CreateBinaryWriter(compressStream))
                        {
                            //var serializer = new DataContractSerializer(type);
                            //xml.WriteStartDocument();
                            //serializer.WriteObject(binaryDictionaryWriter, value);

                            var serializer = new DataContractSerializer(type);

                            serializer.WriteStartObject(xml, value);
                            xml.WriteAttributeString("xmlns", "z", "http://www.w3.org/2000/xmlns/", "http://schemas.microsoft.com/2003/10/Serialization/");
                            serializer.WriteObjectContent(xml, value);
                            serializer.WriteEndObject(xml);
                        }
                    }

                    string newPath = Path.Combine(directoryPath, name + ".v2");
                    string bakPath = Path.Combine(directoryPath, name + ".v2.bak");

                    if (File.Exists(newPath))
                    {
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }

                        File.Move(newPath, bakPath);
                    }

                    File.Move(uniquePath, newPath);

                    {
                        foreach (var extension in new string[] { ".gz", ".gz.bak" })
                        {
                            string deleteFilePath = Path.Combine(directoryPath, name + extension);

                            if (File.Exists(deleteFilePath))
                            {
                                File.Delete(deleteFilePath);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(e);
                }
            }

            sw.Stop();
            Debug.WriteLine("Settings Save {0} {1}", Path.GetFileName(directoryPath), sw.ElapsedMilliseconds);
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
