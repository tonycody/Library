using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

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
                var sourceDirectoryPath = args[1];
                var targetDirectoryPath = args[2];
                var runExePath = args[3];
                var zipFilePath = args[4];

                try
                {
                    Process process = Process.GetProcessById(pid);
                    process.WaitForExit();
                }
                catch (Exception)
                {

                }

                {
                    var temp = Program.GetUniqueDirectoryPath(targetDirectoryPath);
                    Program.CopyDirectory(targetDirectoryPath, temp);

                    bool flag = false;
                    Random random = new Random();
                    string errorInfo = "";

                    for (int i = 0; i < 100; i++)
                    {
                        try
                        {
                            foreach (var path in Directory.GetFiles(targetDirectoryPath, "*", SearchOption.AllDirectories).OrderBy(n => random.Next()))
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

                    Program.CopyDirectory(sourceDirectoryPath, targetDirectoryPath);
                    Directory.Delete(temp, true);
                }

                Directory.Delete(sourceDirectoryPath, true);

                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        if (File.Exists(zipFilePath))
                            File.Delete(zipFilePath);

                        break;
                    }
                    catch (Exception)
                    {

                    }

                    Thread.Sleep(1000);
                }

                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = runExePath;
                    startInfo.WorkingDirectory = Path.GetDirectoryName(runExePath);

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
