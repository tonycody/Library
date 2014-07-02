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
                    var tempDirectoryPath = Program.GetUniqueDirectoryPath(targetDirectoryPath);

                    for (int i = 0; i < 128; i++)
                    {
                        try
                        {
                            Program.CopyDirectory(targetDirectoryPath, tempDirectoryPath);
                            Program.DeleteDirectory(targetDirectoryPath);

                            break;
                        }
                        catch (Exception)
                        {

                        }

                        Thread.Sleep(1000);
                    }

                    for (int i = 0; i < 128; i++)
                    {
                        try
                        {
                            Program.CopyDirectory(sourceDirectoryPath, targetDirectoryPath);
                            Program.DeleteDirectory(sourceDirectoryPath);

                            break;
                        }
                        catch (Exception)
                        {

                        }

                        Thread.Sleep(1000);
                    }

                    for (int i = 0; i < 128; i++)
                    {
                        try
                        {
                            Program.DeleteDirectory(tempDirectoryPath);

                            break;
                        }
                        catch (Exception)
                        {

                        }

                        Thread.Sleep(1000);
                    }
                }

                for (int i = 0; i < 128; i++)
                {
                    try
                    {
                        if (File.Exists(zipFilePath))
                        {
                            File.Delete(zipFilePath);
                        }

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

        public static void DeleteDirectory(string target_dir)
        {
            foreach (string file in Directory.GetFiles(target_dir))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in Directory.GetDirectories(target_dir))
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
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
