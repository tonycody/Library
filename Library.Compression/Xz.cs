using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Library.Io;

namespace Library.Compression
{
    public static class Xz
    {
        public static void Compress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            ProcessStartInfo info;

            if (System.Environment.Is64BitProcess)
            {
                info = new ProcessStartInfo("Assembly/xz_x86-64.exe");
            }
            else
            {
                info = new ProcessStartInfo("Assembly/xz_i486.exe");
            }

            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.Arguments = "-z --format=xz -4 --threads=1 -c";

            using (Process process = Process.Start(info))
            {
                process.PriorityClass = ProcessPriorityClass.Idle;

                Exception threadException = null;

                var thread = new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        byte[] buffer = bufferManager.TakeBuffer(1024 * 32);

                        try
                        {
                            using (var standardOutputStream = process.StandardOutput.BaseStream)
                            {
                                int length = 0;

                                while (0 != (length = standardOutputStream.Read(buffer, 0, buffer.Length)))
                                {
                                    outStream.Write(buffer, 0, length);
                                }
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                    catch (Exception e)
                    {
                        threadException = e;
                    }
                }));
                thread.IsBackground = true;
                thread.Start();

                try
                {
                    byte[] buffer = bufferManager.TakeBuffer(1024 * 32);

                    try
                    {
                        using (var standardInputStream = process.StandardInput.BaseStream)
                        {
                            int length = 0;

                            while (0 != (length = inStream.Read(buffer, 0, buffer.Length)))
                            {
                                standardInputStream.Write(buffer, 0, length);
                            }
                        }
                    }
                    finally
                    {
                        bufferManager.ReturnBuffer(buffer);
                    }
                }
                catch (Exception)
                {
                    throw;
                }

                thread.Join();
                if (threadException != null) throw threadException;

                process.WaitForExit();
            }
        }

        public static void Decompress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            ProcessStartInfo info;

            if (System.Environment.Is64BitProcess)
            {
                info = new ProcessStartInfo("Assembly/xz_x86-64.exe");
            }
            else
            {
                info = new ProcessStartInfo("Assembly/xz_i486.exe");
            }

            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.Arguments = "-d --memlimit-decompress=256MiB";

            using (Process process = Process.Start(info))
            {
                process.PriorityClass = ProcessPriorityClass.Idle;

                Exception threadException = null;

                var thread = new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        byte[] buffer = bufferManager.TakeBuffer(1024 * 32);

                        try
                        {
                            using (var standardOutputStream = process.StandardOutput.BaseStream)
                            {
                                int length = 0;

                                while (0 != (length = standardOutputStream.Read(buffer, 0, buffer.Length)))
                                {
                                    outStream.Write(buffer, 0, length);
                                }
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                    catch (Exception e)
                    {
                        threadException = e;
                    }
                }));
                thread.IsBackground = true;
                thread.Start();

                try
                {
                    byte[] buffer = bufferManager.TakeBuffer(1024 * 32);

                    try
                    {
                        using (var standardInputStream = process.StandardInput.BaseStream)
                        {
                            int length = 0;

                            while (0 != (length = inStream.Read(buffer, 0, buffer.Length)))
                            {
                                standardInputStream.Write(buffer, 0, length);
                            }
                        }
                    }
                    finally
                    {
                        bufferManager.ReturnBuffer(buffer);
                    }
                }
                catch (Exception)
                {
                    throw;
                }

                thread.Join();
                if (threadException != null) throw threadException;

                process.WaitForExit();
            }
        }
    }
}
