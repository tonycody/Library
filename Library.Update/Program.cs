using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

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
            if (args.Length != 4) return;

            try
            {
                if (args[0] != "")
                {
                    try
                    {
                        Process process = Process.GetProcessById(int.Parse(args[0]));
                        process.WaitForExit();
                    }
                    catch
                    {

                    }
                }

                if (Directory.Exists(args[2]))
                    Directory.Delete(args[2], true);

                Program.CopyDirectory(args[1], args[2]);
                Directory.Delete(args[1], true);

                if (args[3] != "")
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = args[3];
                    startInfo.WorkingDirectory = Path.GetDirectoryName(args[3]);

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
    }
}
