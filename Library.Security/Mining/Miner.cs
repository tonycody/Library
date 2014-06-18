using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;

namespace Library.Security
{
    public class Miner
    {
        private CashAlgorithm _cashAlgorithm;
        private TimeSpan _computationTime;

        private bool _isCanceled;

        public Miner(CashAlgorithm cashAlgorithm, TimeSpan computationTime)
        {
            _cashAlgorithm = cashAlgorithm;
            _computationTime = computationTime;
        }

        public CashAlgorithm CashAlgorithm
        {
            get
            {
                return _cashAlgorithm;
            }
        }

        public TimeSpan ComputationTime
        {
            get
            {
                return _computationTime;
            }
        }

        public void Cancel()
        {
            _isCanceled = true;
        }

        public static Cash Create(Miner miner, Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (miner == null || miner.ComputationTime <= TimeSpan.Zero) return null;

            if (miner.CashAlgorithm == CashAlgorithm.Version1)
            {
                miner._isCanceled = false;

                var minerUtilities = new MinerUtilities();

                try
                {
                    var task = Task.Factory.StartNew(() =>
                    {
                        var key = minerUtilities.Create_1(Sha512.ComputeHash(stream), miner.ComputationTime);
                        return new Cash(CashAlgorithm.Version1, key);
                    });

                    while (!task.IsCompleted)
                    {
                        if (miner._isCanceled) minerUtilities.Cancel();

                        Thread.Sleep(1000);
                    }

                    return task.Result;
                }
                catch (AggregateException e)
                {
                    throw e.InnerExceptions.FirstOrDefault();
                }
            }

            return null;
        }

        public static int Verify(Cash cash, Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (cash == null) return 0;

            if (cash.CashAlgorithm == CashAlgorithm.Version1)
            {
                var minerUtilities = new MinerUtilities();

                return minerUtilities.Verify_1(cash.Key, Sha512.ComputeHash(stream));
            }

            return 0;
        }

        private class MinerUtilities
        {
            private static string _path;

            static MinerUtilities()
            {
                OperatingSystem osInfo = Environment.OSVersion;

                if (osInfo.Platform == PlatformID.Win32NT)
                {
                    if (System.Environment.Is64BitProcess)
                    {
                        _path = "Assemblies/Hashcash_x64.exe";
                    }
                    else
                    {
                        _path = "Assemblies/Hashcash_x86.exe";
                    }
                }

                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_path)))
                {
                    try
                    {
                        if (p.MainModule.FileName == Path.GetFullPath(_path))
                        {
                            try
                            {
                                p.Kill();
                                p.WaitForExit();
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }

            private LockedList<Process> _processes = new LockedList<Process>();

            public byte[] Create_1(byte[] value, TimeSpan computationTime)
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value.Length != 64) throw new ArgumentOutOfRangeException("value");
                if (computationTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException("computationTime");

                var info = new ProcessStartInfo(_path);
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;

                info.Arguments = string.Format("hashcash1 create {0} {1}", NetworkConverter.ToHexString(value), (int)computationTime.TotalSeconds);

                using (var process = Process.Start(info))
                {
                    _processes.Add(process);

                    try
                    {
                        process.PriorityClass = ProcessPriorityClass.Idle;

                        try
                        {
                            var result = process.StandardOutput.ReadLine();

                            process.WaitForExit();
                            if (process.ExitCode != 0) throw new MinerException();

                            return NetworkConverter.FromHexString(result);
                        }
                        catch (MinerException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            throw new MinerException(e.Message, e);
                        }
                    }
                    finally
                    {
                        _processes.Remove(process);
                    }
                }
            }

            public int Verify_1(byte[] key, byte[] value)
            {
                if (key == null) throw new ArgumentNullException("key");
                if (key.Length != 64) throw new ArgumentOutOfRangeException("key");
                if (value == null) throw new ArgumentNullException("value");
                if (value.Length != 64) throw new ArgumentOutOfRangeException("value");

                var info = new ProcessStartInfo(_path);
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;

                info.Arguments = string.Format("hashcash1 verify {0} {1}", NetworkConverter.ToHexString(key), NetworkConverter.ToHexString(value));

                using (var process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    try
                    {
                        var result = process.StandardOutput.ReadLine();

                        process.WaitForExit();
                        if (process.ExitCode != 0) return 0;

                        return int.Parse(result);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
            }

            public void Cancel()
            {
                foreach (var process in _processes.ToArray())
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }
    }

    [Serializable]
    class MinerException : Exception
    {
        public MinerException() : base() { }
        public MinerException(string message) : base(message) { }
        public MinerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
