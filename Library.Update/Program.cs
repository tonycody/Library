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

                Directory.Move(args[1], args[2]);

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
    }
}
