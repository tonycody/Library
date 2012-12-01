using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;

namespace Library.Update
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length != 5) return;

            try
            {
                var pid = int.Parse(args[0]);
                var source = args[1];
                var target = args[2];
                var runPath = args[3];
                var zipPath = args[4];

                try
                {
                    Process process = Process.GetProcessById(pid);
                    process.WaitForExit();
                }
                catch (Exception)
                {

                }

                {
                    var temp = GetUniqueDirectoryPath(target);
                    Program.CopyDirectory(target, temp);

                    bool flag = false;
                    Random random = new Random();
                    string errorInfo = "";

                    for (int i = 0; i < 100; i++)
                    {
                        try
                        {
                            foreach (var path in Directory.GetFiles(target, "*", SearchOption.AllDirectories).OrderBy(n => random.Next()))
                            {
                                errorInfo = path;

                                File.Delete(path);
                            }

                            flag = true;

                            break;
                        }
                        catch (Exception)
                        {

                        }

                        Thread.Sleep(1000);
                    }

                    if (!flag) throw new Exception(errorInfo);

                    Program.CopyDirectory(source, target);
                    Directory.Delete(temp, true);
                }

                Directory.Delete(source, true);

                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        if (File.Exists(zipPath))
                            File.Delete(zipPath);

                        break;
                    }
                    catch (Exception)
                    {

                    }

                    Thread.Sleep(1000);
                }

                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = runPath;
                    startInfo.WorkingDirectory = Path.GetDirectoryName(runPath);

                    Process.Start(startInfo);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Library.Update Error", MessageBoxButtons.OK);
            }
        }

        public static void CopyDirectory(string sourceDirectoryPath, string destDirectoryPath)
        {
            if (!Directory.Exists(destDirectoryPath))
            {
                Directory.CreateDirectory(destDirectoryPath);
                File.SetAttributes(destDirectoryPath, File.GetAttributes(sourceDirectoryPath));
            }

            foreach (string file in Directory.GetFiles(sourceDirectoryPath))
            {
                File.Copy(file, Path.Combine(destDirectoryPath, Path.GetFileName(file)), true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDirectoryPath))
            {
                CopyDirectory(dir, Path.Combine(destDirectoryPath, Path.GetFileName(dir)));
            }
        }

        private static string GetUniqueDirectoryPath(string path)
        {
            if (!Directory.Exists(path))
            {
                return path;
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(
                    @"{0}\{1} ({2})",
                    Path.GetDirectoryName(path),
                    Path.GetFileName(path),
                    index);

                if (!Directory.Exists(text))
                {
                    return text;
                }
            }
        }
    }
}
