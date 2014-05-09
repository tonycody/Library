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
            ProcessStartInfo info = null;

            {
                OperatingSystem osInfo = Environment.OSVersion;

                if (osInfo.Platform == PlatformID.Win32NT)
                {
                    if (System.Environment.Is64BitProcess)
                    {
                        info = new ProcessStartInfo("Assembly/xz_x86-64.exe");
                    }
                    else
                    {
                        info = new ProcessStartInfo("Assembly/xz_i486.exe");
                    }
                }
            }

            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            info.Arguments = "--compress --format=xz -6 --threads=1 --stdout";

            using (var inCacheStream = new CacheStream(inStream, 1024 * 4, bufferManager))
            using (var outCacheStream = new CacheStream(outStream, 1024 * 4, bufferManager))
            {
                using (Process process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        Log.Error(e.Data);
                    };

                    Exception threadException = null;

                    var thread = new Thread(new ThreadStart(() =>
                    {
                        try
                        {
                            byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                            try
                            {
                                using (var standardOutputStream = process.StandardOutput.BaseStream)
                                {
                                    int length = 0;

                                    while (0 != (length = standardOutputStream.Read(buffer, 0, buffer.Length)))
                                    {
                                        outCacheStream.Write(buffer, 0, length);
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
                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            using (var standardInputStream = process.StandardInput.BaseStream)
                            {
                                int length = 0;

                                while (0 != (length = inCacheStream.Read(buffer, 0, buffer.Length)))
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

        public static void Decompress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            ProcessStartInfo info = null;

            {
                OperatingSystem osInfo = Environment.OSVersion;

                if (osInfo.Platform == PlatformID.Win32NT)
                {
                    if (System.Environment.Is64BitProcess)
                    {
                        info = new ProcessStartInfo("Assembly/xz_x86-64.exe");
                    }
                    else
                    {
                        info = new ProcessStartInfo("Assembly/xz_i486.exe");
                    }
                }
            }

            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            info.Arguments = "--decompress --format=xz --memlimit-decompress=256MiB --stdout";

            using (var inCacheStream = new CacheStream(inStream, 1024 * 4, bufferManager))
            using (var outCacheStream = new CacheStream(outStream, 1024 * 4, bufferManager))
            {
                using (Process process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        Log.Error(e.Data);
                    };

                    Exception threadException = null;

                    var thread = new Thread(new ThreadStart(() =>
                    {
                        try
                        {
                            byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                            try
                            {
                                using (var standardOutputStream = process.StandardOutput.BaseStream)
                                {
                                    int length = 0;

                                    while (0 != (length = standardOutputStream.Read(buffer, 0, buffer.Length)))
                                    {
                                        outCacheStream.Write(buffer, 0, length);
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
                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            using (var standardInputStream = process.StandardInput.BaseStream)
                            {
                                int length = 0;

                                while (0 != (length = inCacheStream.Read(buffer, 0, buffer.Length)))
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
}
