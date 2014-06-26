using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using Library.Security;

namespace Library.Tools
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "DigitalSignature_Create")
                {
                    var path = args[2];
                    var signPath = args[1];

                    DigitalSignature digitalSignature;

                    using (FileStream inStream = new FileStream(signPath, FileMode.Open))
                    {
                        digitalSignature = DigitalSignatureConverter.FromDigitalSignatureStream(inStream);
                    }

                    using (FileStream inStream = new FileStream(path, FileMode.Open))
                    using (FileStream outStream = new FileStream(path + ".certificate", FileMode.Create))
                    {
                        var certificate = DigitalSignature.CreateFileCertificate(digitalSignature, inStream.Name, inStream);

                        using (var certificateStream = CertificateConverter.ToCertificateStream(certificate))
                        {
                            var buffer = new byte[1024 * 4];

                            int i = -1;

                            while ((i = certificateStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                outStream.Write(buffer, 0, i);
                            }
                        }
                    }
                }
                else if (args.Length >= 2 && args[0] == "DigitalSignature_Verify")
                {
                    var path = args[2];
                    var signPath = args[1];

                    Certificate certificate;

                    using (FileStream inStream = new FileStream(signPath, FileMode.Open))
                    {
                        certificate = CertificateConverter.FromCertificateStream(inStream);
                    }

                    using (FileStream inStream = new FileStream(path, FileMode.Open))
                    {
                        MessageBox.Show(DigitalSignature.VerifyFileCertificate(certificate, inStream.Name, inStream).ToString());
                    }
                }
                else if (args.Length >= 4 && args[0] == "define")
                {
                    List<string> list = new List<string>();

                    using (FileStream inStream = new FileStream(args[2], FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    {
                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;

                            list.Add(line);
                        }
                    }

                    bool flag = (args[1] == "on");

                    foreach (var item in list)
                    {
                        Program.Define(item, flag, args[3]);
                    }
                }
                else if (args.Length >= 3 && args[0] == "increment")
                {
                    //{
                    //    var path = args[1];
                    //    bool flag = false;

                    //    using (var stream = new FileStream(path, FileMode.Open))
                    //    {
                    //        byte[] b = new byte[3];
                    //        stream.Read(b, 0, b.Length);

                    //        flag = CollectionUtilities.Equals(b, new byte[] { 0xEF, 0xBB, 0xBF });
                    //    }

                    //    if (!flag) goto End;

                    //    string newPath;

                    //    using (var reader = new StreamReader(path))
                    //    using (var newStream = Program.GetUniqueFileStream(path))
                    //    using (var writer = new StreamWriter(newStream, new UTF8Encoding(false)))
                    //    {
                    //        newPath = newStream.Name;

                    //        writer.Write(reader.ReadToEnd());
                    //    }

                    //    File.Delete(path);
                    //    File.Move(newPath, path);

                    //End: ;
                    //}

                    string baseDirectory = Path.GetDirectoryName(args[1]);
                    List<string> filePaths = new List<string>();

                    using (Stream stream = new FileStream(args[1], FileMode.Open))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "Compile")
                                {
                                    var path = xml.GetAttribute("Include");
                                    string dependentUponBaseDirectory = Path.GetDirectoryName(path);
                                    filePaths.Add(HttpUtility.UrlDecode(Path.Combine(baseDirectory, path)));

                                    using (var xmlSubtree = xml.ReadSubtree())
                                    {
                                        while (xmlSubtree.Read())
                                        {
                                            if (xmlSubtree.NodeType == XmlNodeType.Element)
                                            {
                                                if (xmlSubtree.LocalName == "DependentUpon")
                                                {
                                                    filePaths.Add(HttpUtility.UrlDecode(Path.Combine(Path.Combine(baseDirectory, dependentUponBaseDirectory), xml.ReadString())));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    filePaths.Remove(args[2]);
                    filePaths.Sort();

                    Regex regex = new Regex(@"^( *)\[( *)assembly( *):( *)AssemblyVersion( *)\(( *)" + "\"" + @"(\d*)\.(\d*)\.(\d*)\.(\d*)" + "\"" + @"( *)\)( *)\](.*)$");
                    byte[] hash = Program.GetHash(filePaths);
                    bool rewrite = false;

                    using (var readerStream = new StreamReader(args[2]))
                    using (var writerStream = new StreamWriter(args[2] + "~", false, new UTF8Encoding(false)))
                    {
                        for (; ; )
                        {
                            var line = readerStream.ReadLine();
                            if (line == null)
                            {
                                break;
                            }

                            var match = regex.Match(line);

                            if (match.Success)
                            {
                                int i = int.Parse(match.Groups[10].Value);

                                if (match.Groups[13].Value.TrimStart().StartsWith("//"))
                                {
                                    if (!Program.ArrayEquals(hash, Convert.FromBase64String(match.Groups[13].Value.TrimStart().Remove(0, 2).Trim())))
                                    {
                                        i++;
                                        rewrite = true;
                                    }
                                }
                                else
                                {
                                    rewrite = true;
                                }

                                writerStream.WriteLine(
                                string.Format(
                                    "{0}[{1}assembly{2}:{3}AssemblyVersion{4}({5}\"{6}.{7}.{8}.{9}\"{10}){11}]{12}",
                                    match.Groups[1].Value,
                                    match.Groups[2].Value,
                                    match.Groups[3].Value,
                                    match.Groups[4].Value,
                                    match.Groups[5].Value,
                                    match.Groups[6].Value,
                                    match.Groups[7].Value,
                                    match.Groups[8].Value,
                                    match.Groups[9].Value,
                                    i.ToString(),
                                    match.Groups[11].Value,
                                    match.Groups[12].Value,
                                    " // " + Convert.ToBase64String(hash)));
                            }
                            else
                            {
                                writerStream.WriteLine(line);
                            }
                        }
                    }

                    if (rewrite)
                    {
                        File.Delete(args[2]);
                        File.Move(args[2] + "~", args[2]);
                    }
                    else
                    {
                        File.Delete(args[2] + "~");
                    }
                }
                else if (args.Length >= 1 && args[0] == "settings")
                {
                    string settingsPath = args[1];

                    StringBuilder builder = new StringBuilder();
                    StringBuilder builder2 = new StringBuilder();
                    Regex regex = new Regex("new Library\\.Configuration\\.SettingContent<(.*)>\\(\\) { Name = \"(.*)\", Value = .* },(.*)$");

                    using (FileStream inStream = new FileStream(settingsPath, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    {
                        bool isRead = false;

                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null)
                            {
                                break;
                            }

                            if (line.Contains("new Library.Configuration.SettingContent"))
                            {
                                builder2.AppendLine(line);
                                isRead = true;
                            }
                            else if (isRead && line.Trim() == "")
                            {
                                builder2.AppendLine("");
                            }
                            else if (isRead)
                            {
                                break;
                            }
                        }
                    }

                    foreach (var item in builder2.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.None))
                    {
                        if (item.Trim() == "")
                        {
                            builder.AppendLine("");
                        }
                        else
                        {
                            Match match = regex.Match(item);

                            var attributeBuilder = new StringBuilder();

                            {
                                var text = match.Groups[3].Value;

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    text = text.Trim().TrimStart('/').Replace("]", "]\n");

                                    foreach (var line in text.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(n => n.Trim()))
                                    {
                                        attributeBuilder.AppendLine("        " + line);
                                    }
                                }
                            }

                            builder.AppendLine(attributeBuilder.ToString() + string.Format(
                                "        public {0} {1}\r\n" +
                                "        {{\r\n" +
                                "            get\r\n" +
                                "            {{\r\n" +
                                "                lock (this.ThisLock)\r\n" +
                                "                {{\r\n" +
                                "                   return ({0})this[\"{1}\"];\r\n" +
                                "                }}\r\n" +
                                "            }}\r\n" +
                                "            set\r\n" +
                                "            {{\r\n" +
                                "                lock (this.ThisLock)\r\n" +
                                "                {{\r\n" +
                                "                    this[\"{1}\"] = value;\r\n" +
                                "                }}\r\n" +
                                "            }}\r\n" +
                                "        }}\r\n",
                                match.Groups[1].Value,
                                match.Groups[2].Value));
                        }
                    }

                    using (FileStream inStream = new FileStream(settingsPath, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    using (FileStream outStream = new FileStream(settingsPath + ".tmp", FileMode.Create))
                    using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(false)))
                    {
                        bool isRegion = false;
                        bool isRewrite = false;

                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null)
                            {
                                break;
                            }

                            if (!isRewrite)
                            {
                                if (line.Contains("#region Property"))
                                {
                                    isRegion = true;
                                }
                                else if (line.Contains("#endregion"))
                                {
                                    writer.Write("        #region Property\r\n\r\n" +
                                        builder.ToString().Trim('\r', '\n') +
                                        "\r\n\r\n");

                                    isRegion = false;
                                    isRewrite = true;
                                }
                            }

                            if (!isRegion)
                            {
                                writer.WriteLine(line);
                            }
                        }
                    }

                    File.Delete(settingsPath);
                    File.Move(settingsPath + ".tmp", settingsPath);
                }
                else if (args.Length >= 3 && args[0] == "languages")
                {
                    string languageManagerPath = args[1];
                    string languageXmlPath = Path.Combine(args[2], "English.xml");
                    StringBuilder builder = new StringBuilder();

                    using (FileStream stream = new FileStream(languageXmlPath, FileMode.Open))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        try
                        {
                            while (xml.Read())
                            {
                                if (xml.NodeType == XmlNodeType.Element)
                                {
                                    if (xml.LocalName == "Translate")
                                    {
                                        builder.AppendLine(string.Format(
                                            "        public string {0}\r\n" +
                                            "        {{\r\n" +
                                            "            get\r\n" +
                                            "            {{\r\n" +
                                            "                lock (this.ThisLock)\r\n" +
                                            "                {{\r\n" +
                                            "                    return this.Translate(\"{0}\");\r\n" +
                                            "                }}\r\n" +
                                            "            }}\r\n" +
                                            "        }}\r\n",
                                        xml.GetAttribute("Key")));
                                    }
                                }
                                else if (xml.NodeType == XmlNodeType.Whitespace)
                                {
                                    if (xml.Value.StartsWith("\r\n\r\n"))
                                    {
                                        builder.AppendLine("");
                                    }
                                }
                            }
                        }
                        catch (XmlException)
                        {

                        }
                    }

                    using (FileStream inStream = new FileStream(languageManagerPath, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    using (FileStream outStream = new FileStream(languageManagerPath + ".tmp", FileMode.Create))
                    using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(false)))
                    {
                        bool isRegion = false;
                        bool isRewrite = false;

                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null)
                            {
                                break;
                            }

                            if (!isRewrite)
                            {
                                if (line.Contains("#region Property"))
                                {
                                    isRegion = true;
                                }
                                else if (line.Contains("#endregion"))
                                {
                                    writer.Write("        #region Property\r\n\r\n" +
                                        builder.ToString().Trim('\r', '\n') +
                                        "\r\n\r\n");

                                    isRegion = false;
                                    isRewrite = true;
                                }
                            }

                            if (!isRegion)
                            {
                                writer.WriteLine(line);
                            }
                        }
                    }

                    File.Delete(languageManagerPath);
                    File.Move(languageManagerPath + ".tmp", languageManagerPath);

                    Program.LanguageSetting(languageXmlPath);
                }
                else if (args.Length >= 3 && args[0] == "CodeClone")
                {
                    string pathListPath = args[1];
                    string wordListPath = args[2];

                    Dictionary<string, string> pathDic = new Dictionary<string, string>();

                    using (FileStream inStream = new FileStream(pathListPath, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    {
                        var tempList = new List<string>();

                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;

                            tempList.Add(line);

                            if (tempList.Count == 2)
                            {
                                pathDic[tempList[0]] = tempList[1];

                                reader.ReadLine(); //空白読み捨て
                                tempList.Clear();
                            }
                        }
                    }

                    Dictionary<string, string> wordDic = new Dictionary<string, string>();

                    using (FileStream inStream = new FileStream(wordListPath, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    {
                        var tempList = new List<string>();

                        for (; ; )
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;

                            tempList.Add(line);

                            if (tempList.Count == 2)
                            {
                                wordDic[tempList[0]] = tempList[1];

                                reader.ReadLine(); //空白読み捨て
                                tempList.Clear();
                            }
                        }
                    }

                    foreach (var item in pathDic)
                    {
                        var sourcePath = item.Key;
                        var targetPath = item.Value;

                        using (FileStream inStream = new FileStream(sourcePath, FileMode.Open))
                        using (StreamReader reader = new StreamReader(inStream))
                        using (FileStream outStream = new FileStream(targetPath, FileMode.Create))
                        using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(false)))
                        {
                            StringBuilder sb = new StringBuilder(reader.ReadToEnd());

                            foreach (var word in wordDic)
                            {
                                sb.Replace(word.Key, word.Value);
                            }

                            writer.Write(sb.ToString());
                        }
                    }
                }
                else if (args.Length >= 2 && args[0] == "linecount")
                {
                    string basePath = args[1];
                    int count = 0;

                    var list = new List<KeyValuePair<int, string>>();

                    foreach (var path in Program.GetFiles(basePath))
                    {
                        int tcount = 0;
                        using (StreamReader reader = new StreamReader(path))
                        {
                            tcount = reader.ReadToEnd().Count(n => n == '\n');
                        }

                        list.Add(new KeyValuePair<int, string>(tcount, path));
                        count += tcount;
                    }

                    list.Sort((KeyValuePair<int, string> kvp1, KeyValuePair<int, string> kvp2) =>
                    {
                        return kvp1.Key.CompareTo(kvp2.Key);
                    });

                    foreach (var item in list)
                    {
                        var text = item.Value.Substring(basePath.Length).Replace(@"\", "/");
                        Console.WriteLine(string.Format("{0}\t{1}", item.Key, text));
                    }

                    Console.WriteLine(count);
                }
                else if (args.Length >= 3 && args[0] == "run")
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = Path.Combine(Directory.GetCurrentDirectory(), args[1]);
                    startInfo.WorkingDirectory = Path.GetFullPath(args[2]);

                    Process.Start(startInfo);
                }
                else if (args.Length >= 2 && args[0] == "Template")
                {
                    var settings = new List<List<string>>();

                    using (StreamReader reader = new StreamReader(args[1], Encoding.UTF8))
                    {
                        string line;

                        do
                        {
                            var list = new List<string>();

                            while (!string.IsNullOrWhiteSpace(line = reader.ReadLine()))
                            {
                                list.Add(line);
                            }

                            if (list.Count > 0) settings.Add(list);

                        } while (line != null);
                    }

                    foreach (var setting in settings)
                    {
                        if (setting.Count < 2) continue;

                        var sourcePath = setting[0];

                        foreach (var item in setting.Skip(1))
                        {
                            string text;

                            using (StreamReader reader = new StreamReader(sourcePath, Encoding.UTF8))
                            {
                                text = reader.ReadToEnd();
                            }

                            var commands = Decode(item).ToList();
                            if (commands.Count < 2) continue;

                            var targetPath = commands[0];

                            int count = 1;

                            foreach (var item2 in commands.Skip(1))
                            {
                                text = Regex.Replace(text, string.Format(@"<#\s*{0}\s*#>", count++), item2);
                            }

                            using (StreamWriter writer = new StreamWriter(targetPath))
                            {
                                writer.Write(text);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Library.Tool Error", MessageBoxButtons.OK);
                MessageBox.Show(e.StackTrace, "Library.Tool Error", MessageBoxButtons.OK);
            }
        }

        private static void Define(string path, bool on, string name)
        {
            Regex regex = new Regex(@"(.*)#(.*)define(\s*)(?<name>\S*)(.*)");

            List<string> items = new List<string>();
            StringBuilder content = new StringBuilder();

            if (on)
            {
                bool writed = false;

                using (FileStream inStream = new FileStream(path, FileMode.Open))
                using (StreamReader reader = new StreamReader(inStream))
                {
                    string line;
                    bool flag = false;

                    while (null != (line = reader.ReadLine()))
                    {
                        if (!flag)
                        {
                            var match = regex.Match(line);

                            if (match.Success)
                            {
                                items.Add(line);

                                if (match.Groups["name"].Value == name)
                                {
                                    writed = true;
                                }
                            }
                            else
                            {
                                content.AppendLine(line);

                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    flag = true;
                                }
                            }
                        }
                        else
                        {
                            content.AppendLine(line);
                        }
                    }
                }

                if (!writed)
                {
                    items.Add("#define " + name);
                }
            }
            else
            {
                using (FileStream inStream = new FileStream(path, FileMode.Open))
                using (StreamReader reader = new StreamReader(inStream))
                {
                    string line;
                    bool flag = false;

                    while (null != (line = reader.ReadLine()))
                    {
                        if (!flag)
                        {
                            var match = regex.Match(line);

                            if (match.Success)
                            {
                                if (match.Groups["name"].Value != name)
                                {
                                    items.Add(line);
                                }
                            }
                            else
                            {
                                content.AppendLine(line);

                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    flag = true;
                                }
                            }
                        }
                        else
                        {
                            content.AppendLine(line);
                        }
                    }
                }
            }

            using (FileStream outStream = new FileStream(path + ".tmp", FileMode.Create))
            using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(false)))
            {
                if (items.Count != 0)
                {
                    foreach (var line in items)
                    {
                        writer.WriteLine(line);
                    }

                    writer.WriteLine();
                }

                writer.Write(content.ToString().TrimStart('\r', '\n'));
            }

            File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        private static void LanguageSetting(string languageXmlPath)
        {
            var directoryPath = Path.GetDirectoryName(languageXmlPath);

            if (!Directory.Exists(directoryPath)) return;

            Dictionary<string, Dictionary<string, string>> _dic = new Dictionary<string, Dictionary<string, string>>();

            foreach (string path in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (languageXmlPath == path) continue;

                Dictionary<string, string> dic = new Dictionary<string, string>();

                using (XmlTextReader xml = new XmlTextReader(path))
                {
                    try
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "Translate")
                                {
                                    dic[xml.GetAttribute("Key")] = xml.GetAttribute("Value");
                                }
                            }
                        }
                    }
                    catch (XmlException)
                    {

                    }
                }

                _dic[Path.GetFileNameWithoutExtension(path)] = dic;
            }

            foreach (string path in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (languageXmlPath == path) continue;

                StringBuilder builder = new StringBuilder();

                using (FileStream stream = new FileStream(languageXmlPath, FileMode.Open))
                using (XmlTextReader xml = new XmlTextReader(stream))
                {
                    try
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "Translate")
                                {
                                    var key = xml.GetAttribute("Key");
                                    string value = "";

                                    if (!_dic[Path.GetFileNameWithoutExtension(path)].TryGetValue(key, out value))
                                    {
                                        value = xml.GetAttribute("Value");
                                    }

                                    builder.AppendLine(string.Format("  <Translate Key=\"{0}\" Value=\"{1}\" />",
                                        Program.HtmlEncode_Hex(key), Program.HtmlEncode_Hex(value)));
                                }
                            }
                            else if (xml.NodeType == XmlNodeType.Whitespace)
                            {
                                if (xml.Value.StartsWith("\r\n\r\n"))
                                {
                                    builder.AppendLine("");
                                }
                            }
                        }
                    }
                    catch (XmlException)
                    {

                    }
                }

                using (FileStream stream = new FileStream(path, FileMode.Create))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>");
                    writer.WriteLine("<Configuration>");
                    writer.Write(builder.ToString());
                    writer.WriteLine("</Configuration>");
                }
            }
        }

        private static string GetUniqueFileName(string path)
        {
            for (; ; )
            {
                if (!File.Exists(path))
                {
                    return path;
                }

                path = path + "~";
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    return new FileStream(path, FileMode.CreateNew);
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
                        return new FileStream(text, FileMode.CreateNew);
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

        private static byte[] GetHash(IEnumerable<string> filePaths)
        {
            using (var memoryStream = new MemoryStream())
            {
                foreach (var path in filePaths)
                {
                    using (var rstream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    using (var sha512 = SHA512.Create())
                    {
                        var buffer = sha512.ComputeHash(rstream);
                        memoryStream.Write(buffer, 0, buffer.Length);
                    }
                }

                using (var sha512 = SHA512.Create())
                {
                    return sha512.ComputeHash(memoryStream.ToArray());
                }
            }
        }

        private static IEnumerable<string> GetFiles(string directory)
        {
            List<string> list = new List<string>();
            List<string> directoryFilter = new List<string>() { "bin", "obj", ".hg", "test-results" };
            List<string> fileFilter = new List<string>() { ".cs", ".xaml", ".xml", ".py" };

            foreach (var path in System.IO.Directory.GetDirectories(directory))
            {
                if (!directoryFilter.Contains(System.IO.Path.GetFileName(path)))
                {
                    list.AddRange(Program.GetFiles(path));
                }
            }

            foreach (var path in System.IO.Directory.GetFiles(directory))
            {
                if (fileFilter.Contains(System.IO.Path.GetExtension(path)))
                {
                    list.Add(path);
                }
            }

            return list;
        }

        private static bool ArrayEquals(Array b1, Array b2)
        {
            if (b1.Length != b2.Length)
            {
                return false;
            }

            for (int i = 0; i < b1.Length; i++)
            {
                if (!b1.GetValue(i).Equals(b2.GetValue(i)))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> Decode(string option)
        {
            try
            {
                List<char> charList = new List<char>();
                bool wordFlag = false;

                List<string> stringList = new List<string>();

                for (int i = 0; i < option.Length; i++)
                {
                    char w1;
                    char? w2 = null;

                    w1 = option[i];
                    if (option.Length > i + 1) w2 = option[i + 1];

                    if (w1 == '\\' && w2.HasValue)
                    {
                        if (w2.Value == '\"' || w2.Value == '\\')
                        {
                            charList.Add(w2.Value);
                            i++;
                        }
                        else
                        {
                            charList.Add(w1);
                        }
                    }
                    else
                    {
                        if (wordFlag)
                        {
                            if (w1 == '\"')
                            {
                                wordFlag = false;
                            }
                            else
                            {
                                charList.Add(w1);
                            }
                        }
                        else
                        {
                            if (w1 == '\"')
                            {
                                wordFlag = true;
                            }
                            else if (w1 == ' ')
                            {
                                var value = new string(charList.ToArray());

                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    stringList.Add(value);
                                }

                                charList.Clear();
                            }
                            else
                            {
                                charList.Add(w1);
                            }
                        }
                    }
                }

                {
                    var value = new string(charList.ToArray());

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        stringList.Add(value);
                    }

                    charList.Clear();
                }

                return stringList;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string HtmlEncode_Hex(string text)
        {
            if (text == null) return null;

            StringBuilder sb = new StringBuilder();

            var list = System.Web.HttpUtility.HtmlEncode(text).ToCharArray();

            {
                var length = list.Length;

                for (int i = 0; i < length; i++)
                {
                    if (list[i] == '&'
                        && (i + 1) < length && list[i + 1] == '#')
                    {
                        i += 2;

                        string code = "";

                        for (; i < length; i++)
                        {
                            var c = list[i];
                            if (c == ';') break;

                            code += c;
                        }

                        int result;

                        if (int.TryParse(code, out result))
                        {
                            sb.Append("&#x" + String.Format("{0:X4}", result) + ";");
                        }
                        else
                        {
                            sb.Append("&#" + code + ";");
                        }
                    }
                    else
                    {
                        sb.Append(list[i]);
                    }
                }
            }

            return sb.ToString();
        }
    }
}
