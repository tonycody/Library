using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Windows.Forms;

namespace Library.Tool
{
    internal class Program
    {
        internal static void Main(string[] args)
        {
            try
            {
                if (args.Length >= 4 && args[0] == "define")
                {
                    StringBuilder stringBuilder = new StringBuilder();
                    var path = args[2];

                    if (args[1] == "on")
                    {
                        stringBuilder.AppendLine("#define " + args[3]);
                    }

                    using (FileStream inStream = new FileStream(path, FileMode.Open))
                    using (StreamReader reader = new StreamReader(inStream))
                    {
                        bool f = false;
                        string line;

                        while (null != (line = reader.ReadLine()))
                        {
                            if (!f && line.StartsWith("using"))
                            {
                                f = true;

                                var temp = stringBuilder.ToString().Trim('\r', '\n');
                                stringBuilder.Clear();
                                stringBuilder.Append(temp);
                                stringBuilder.AppendLine();
                                stringBuilder.AppendLine();
                            }

                            if (!f && line == ("#define " + args[3]))
                            {

                            }
                            else
                            {
                                stringBuilder.AppendLine(line);
                            }
                        }
                    }

                    using (FileStream outStream = new FileStream(path + ".tmp", FileMode.Create))
                    using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(true)))
                    {
                        writer.Write(stringBuilder.ToString().TrimStart('\r', '\n'));
                    }

                    File.Delete(path);
                    File.Move(path + ".tmp", path);
                }
                else if (args.Length >= 3 && args[0] == "increment")
                {
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
                                    filePaths.Add(Path.Combine(baseDirectory, path));

                                    using (var xmlReader = xml.ReadSubtree())
                                    {
                                        while (xmlReader.Read())
                                        {
                                            if (xmlReader.NodeType == XmlNodeType.Element)
                                            {
                                                if (xmlReader.LocalName == "DependentUpon")
                                                {
                                                    filePaths.Add(Path.Combine(Path.Combine(baseDirectory, dependentUponBaseDirectory), xml.ReadString()));
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
                    using (var writerStream = new StreamWriter(args[2] + "~", false, new UTF8Encoding(true)))
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
                else if (args.Length >= 2 && args[0] == "settings")
                {
                    string settingsPath = args[1];

                    StringBuilder builder = new StringBuilder();
                    StringBuilder builder2 = new StringBuilder();
                    Regex regex = new Regex("new Library\\.Configuration\\.SettingsContext<(.*)>\\(\\) { Name = \"(.*)\", Value = (.*) },");

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

                            if (line.Contains("new Library.Configuration.SettingsContext"))
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

                            builder.AppendLine(string.Format(
                            "        public {0} {1}\r\n" +
                                "        {{\r\n" +
                                "            get\r\n" +
                                "            {{\r\n" +
                                "                lock (this.ThisLock)\r\n" +
                                "                {{\r\n" +
                                "                   return ({0})this[\"{1}\"];\r\n" +
                                "                }}\r\n" +
                                "            }}\r\n\r\n" +
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
                    using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(true)))
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
                    string languageXmlPath = args[2];
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
                    using (StreamWriter writer = new StreamWriter(outStream, new UTF8Encoding(true)))
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
                else if (args.Length >= 3 && args[0] == "hgmove")
                {
                    string basePath = Directory.GetCurrentDirectory();
                    string pathx = args[1];
                    string pathy = args[2];
                    string pathz = Program.GetUniqueFileName("temp");
                    List<string> directoryList = new List<string>();

                    if (!Directory.Exists(basePath))
                    {
                        throw new DirectoryNotFoundException(basePath);
                    }
                    else if (!File.Exists(Path.Combine(basePath, pathy)))
                    {
                        throw new FileNotFoundException(Path.Combine(basePath, pathy));
                    }

                    Directory.SetCurrentDirectory(basePath);

                    if (File.Exists(Path.Combine(basePath, pathx)))
                    {
                        File.Move(Path.Combine(basePath, pathx), Path.Combine(basePath, pathz));
                    }

                    string tempDirectoryPath = Path.GetDirectoryName(Path.Combine(basePath, pathx));

                    while (!Directory.Exists(tempDirectoryPath))
                    {
                        directoryList.Add(tempDirectoryPath);
                        tempDirectoryPath = Path.GetDirectoryName(tempDirectoryPath);
                    }

                    for (int i = directoryList.Count - 1; i >= 0; i--)
                    {
                        Directory.CreateDirectory(directoryList[i]);
                    }

                    File.Move(Path.Combine(basePath, pathy), Path.Combine(basePath, pathx));

                    Process process = new Process();
                    process.StartInfo.WorkingDirectory = basePath;
                    process.StartInfo.Arguments = string.Format("mv \"{0}\" \"{1}\"", pathx, pathy);
                    process.StartInfo.FileName = @"hg";
                    process.Start();
                    process.Close();

                    for (int i = 0; i < directoryList.Count; i++)
                    {
                        if (Directory.Exists(directoryList[i]))
                        {
                            try
                            {
                                Directory.Delete(directoryList[i]);
                            }
                            catch (IOException)
                            {
                            }
                        }
                    }

                    if (File.Exists(Path.Combine(basePath, pathz)))
                    {
                        File.Move(Path.Combine(basePath, pathz), Path.Combine(basePath, pathx));
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
                        var text = item.Value.Substring(basePath.Length + 1).Replace(@"\", "/");
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
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Library.Tool Error", MessageBoxButtons.OK);
            }
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
                                    dic.Add(xml.GetAttribute("Key"), xml.GetAttribute("Value"));
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
                                        System.Web.HttpUtility.HtmlEncode(key), System.Web.HttpUtility.HtmlEncode(value)));
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
                using (StreamWriter writer = new StreamWriter(stream))
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

        private static byte[] GetHash(IEnumerable<string> filePaths)
        {
            using (var memoryStream = new MemoryStream())
            {
                foreach (var path in filePaths)
                {
                    using (var rstream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    using (var sha512 = SHA512Managed.Create())
                    {
                        var buffer = sha512.ComputeHash(rstream);
                        memoryStream.Write(buffer, 0, buffer.Length);
                    }
                }

                using (var sha512 = SHA512Managed.Create())
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
    }
}
