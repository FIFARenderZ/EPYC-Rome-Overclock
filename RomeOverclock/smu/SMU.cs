using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using OpenLibSys;
using RomeOverclock;

[assembly: InternalsVisibleTo("RomeOverclockUnittest"),
    InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace ZenStatesDebugTool
{
    public abstract class SMU
    {
        public enum CPUType : int
        {
            Unsupported = 0,
            DEBUG,
            Rome
        }

        public enum Status : int
        {
            OK = 0x1,
            FAILED = 0xFF,
            UNKNOWN_CMD = 0xFE,
            CMD_REJECTED_PREREQ = 0xFD,
            CMD_REJECTED_BUSY = 0xFC
        }

        public SMU()
        {
            Version = 0;
            // SMU
            SMU_PCI_ADDR = ((0x0 & 0xFF) << 8) | ((0x0 & 0x1F) << 3) | (0x0 & 7); // 0x00000000;
            SMU_PCI_ADDR_2 = ((0xA0 & 0xFF) << 8) | ((0x0 & 0x1F) << 3) | (0x0 & 7); // 0x0000A000;
            SMU_OFFSET_ADDR = 0xB8;
            SMU_OFFSET_DATA = 0xBC;

            SMU_ADDR_MSG = 0x03B10528;
            SMU_ADDR_RSP = 0x03B10564;
            SMU_ADDR_ARG0 = 0x03B10598;
            SMU_ADDR_ARG1 = SMU_ADDR_ARG0 + 0x4;

            // SMU Messages
            SMC_MSG_TestMessage = 0x1;
            SMC_MSG_GetSmuVersion = 0x2;
        }

        public uint Version { get; set; }
        public uint SMU_PCI_ADDR { get; protected set; }
        public uint SMU_PCI_ADDR_2 { get; protected set; }
        public uint SMU_OFFSET_ADDR { get; protected set; }
        public uint SMU_OFFSET_DATA { get; protected set; }

        public uint SMU_ADDR_MSG { get; protected set; }
        public uint SMU_ADDR_RSP { get; protected set; }
        public uint SMU_ADDR_ARG0 { get; protected set; }

        public uint SMU_ADDR_ARG1 { get; protected set; }

        public uint SMC_MSG_TestMessage { get; protected set; }
        public uint SMC_MSG_GetSmuVersion { get; protected set; }
    }

    public class Zen2Settings : SMU
    {
        public Zen2Settings()
        {
            SMU_ADDR_MSG = 0x03B10524;
            SMU_ADDR_RSP = 0x03B10570;
            SMU_ADDR_ARG0 = 0x03B10A40;
            SMU_ADDR_ARG1 = SMU_ADDR_ARG0 + 0x4;
        }
    }

    public static class GetSMUStatus
    {
        private static readonly Dictionary<SMU.Status, String> status = new Dictionary<SMU.Status, string>()
        {
            { SMU.Status.OK, "OK" },
            { SMU.Status.FAILED, "Failed" },
            { SMU.Status.UNKNOWN_CMD, "Unknown Command" },
            { SMU.Status.CMD_REJECTED_PREREQ, "CMD Rejected Prereq" },
            { SMU.Status.CMD_REJECTED_BUSY, "CMD Rejected Busy" }
        };

        public static string GetByType(SMU.Status type)
        {
            if (!status.TryGetValue(type, out string output))
            {
                return "Unknown Status";
            }
            return output;
        }
    }

    public class SMUCommand : IDisposable
    {
        public delegate void LoggerDelegate(string msg);

        public interface IOlsInterface : IDisposable
        {
            uint GetStatus();

            uint GetDllStatus();

            int CpuidPx(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx, UIntPtr processAffinityMask);

            int WritePciConfigDwordEx(uint pciAddress, uint regAddress, uint value);

            int ReadPciConfigDwordEx(uint pciAddress, uint regAddress, ref uint value);
        }

        public enum SMUCmdConstant : uint
        {
            kSetAllCoreVoltage = 0x12,
            kSetSingleCoreFreq = 0x16,
            kSetAllCoreFreq = 0x18,
            kSetFreqLock = 0x24,

            kSetPPT = 0x53,
            kSetTDC = 0x54,
            kSetEDC = 0x55,

            kResetDefaultVoltage = 0x13,
            kResetDefaultFreq = 0x19,
            kResetDefaultFreqLock = 0x25,

            kFullPerf = 0xA,
        }

        private class OlsImpl : IOlsInterface
        {
            private readonly Ols mOls;

            public OlsImpl()
            {
                mOls = new Ols();
            }

            public uint GetStatus()
            {
                return mOls.GetStatus();
            }

            public uint GetDllStatus()
            {
                return mOls.GetDllStatus();
            }

            public int CpuidPx(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx, UIntPtr processAffinityMask)
            {
                return mOls.CpuidPx(index, ref eax, ref ebx, ref ecx, ref edx, processAffinityMask);
            }

            public int WritePciConfigDwordEx(uint pciAddress, uint regAddress, uint value)
            {
                return mOls.WritePciConfigDwordEx(pciAddress, regAddress, value);
            }

            public int ReadPciConfigDwordEx(uint pciAddress, uint regAddress, ref uint value)
            {
                return mOls.ReadPciConfigDwordEx(pciAddress, regAddress, ref value);
            }

            public void Dispose()
            {
                mOls.Dispose();
            }
        }

        private bool mIsDualSocket;
        private readonly IOlsInterface mOls;
        private readonly LoggerDelegate mLogger;
        private readonly SMU mSmu = new Zen2Settings();
        private readonly Mutex mMutexPci = new Mutex();

        public SMUCommand(bool isDualSocket, LoggerDelegate logger)
        {
            mOls = new OlsImpl();
            CheckOlsStatus();

            mIsDualSocket = isDualSocket;
            mLogger = logger;
        }

        internal SMUCommand(IOlsInterface ols, bool isDualSocket, LoggerDelegate logger)
        {
            mOls = ols;
            mIsDualSocket = isDualSocket;
            mLogger = logger;
        }

        public void Dispose()
        {
            mOls.Dispose();
        }

        public bool IsDualSocket
        {
            get { return mIsDualSocket; }
            set { mIsDualSocket = value; }
        }

        public uint CPUID
        {
            get { return GetCpuInfo() & 0xFFFFFFF0; }
        }

        public virtual bool ApplyFrequencyAllCoreSetting(uint frequency)
        {
            return ApplyCommandImpl(SMUCmdConstant.kSetAllCoreFreq, Convert.ToUInt32(frequency), (_) =>
            {
                return $"Set frequency to {frequency}MHz.";
            });
        }

        public virtual bool ApplyPPTSetting(uint val)
        {
            return ApplyCommandImpl(SMUCmdConstant.kSetPPT, Convert.ToUInt32(val * 1000), (_) =>
            {
                return $"Set PPT limit to {val}W.";
            });
        }

        public virtual bool ApplyTDCSetting(uint val)
        {
            return ApplyCommandImpl(SMUCmdConstant.kSetTDC, Convert.ToUInt32(val * 1000), (_) =>
            {
                return $"Set TDC limit to {val}W.";
            });
        }

        public virtual bool ApplyEDCSetting(uint val)
        {
            return ApplyCommandImpl(SMUCmdConstant.kSetEDC, Convert.ToUInt32(val * 1000), (_) =>
            {
                return $"Set EDC limit to {val}W.";
            });
        }

        public static int ToVoltageInteger(decimal vol)
        {
            return (int)((vol - 1.55m) / -0.00625m);
        }

        public static decimal ToVoltageDecimal(int vol)
        {
            return vol * -0.00625m + 1.55m;
        }

        public virtual bool ApplyVoltage(int vol, int min = 12, int max = 128)
        {
            if (vol < min || vol > max) return false;
            return ApplyCommandImpl(SMUCmdConstant.kSetAllCoreVoltage, Convert.ToUInt32(vol), (_) =>
            {
                var voltage = ToVoltageDecimal(vol);
                return $"Set Voltage to {voltage}V.";
            });
        }

        public virtual bool ApplyFreqLock(bool isLocked)
        {
            if (isLocked)
            {
                return ApplyCommandImpl(SMUCmdConstant.kSetFreqLock, 1, (_) =>
                {
                    return "Locked frequencies.";
                });
            }
            else
            {
                return ApplyCommandImpl(SMUCmdConstant.kResetDefaultFreqLock, 1, (_) =>
                {
                    return "Unlocked frequencies.";
                });
            }
        }

        public virtual bool RevertVoltage()
        {
            return ApplyCommandImpl(SMUCmdConstant.kResetDefaultVoltage, 1, (_) =>
            {
                return "Reverted voltage to normal.";
            });
        }

        public bool RevertFrequency()
        {
            return ApplyCommandImpl(SMUCmdConstant.kResetDefaultFreq, 1, (_) =>
            {
                return "Reverted frequency to normal.";
            });
        }

        internal virtual bool ApplyCommandImpl(SMUCmdConstant cmd, uint val, Func<uint, string> msgFunc)
        {
            if (ApplySMUCommand(cmd, val, mSmu.SMU_PCI_ADDR) != SMU.Status.OK)
            {
                mLogger.Invoke($"Socket 1 apply cmd error: {cmd}");
                return false;
            }

            mLogger.Invoke($"Socket 1: {msgFunc.Invoke(val)}");
            if (mIsDualSocket)
            {
                if (ApplySMUCommand(cmd, val, mSmu.SMU_PCI_ADDR_2) != SMU.Status.OK)
                {
                    mLogger.Invoke($"Socket 2 apply cmd error: {cmd}");
                    return false;
                }

                mLogger.Invoke($"Socket 2: {msgFunc.Invoke(val)}");
            }

            return true;
        }

        private void CheckOlsStatus()
        {
            // Check support library status
            switch (mOls.GetStatus())
            {
                case (uint)Ols.Status.NO_ERROR:
                    break;
                case (uint)Ols.Status.DLL_NOT_FOUND:
                    throw new ApplicationException("WinRing DLL_NOT_FOUND");
                case (uint)Ols.Status.DLL_INCORRECT_VERSION:
                    throw new ApplicationException("WinRing DLL_INCORRECT_VERSION");
                case (uint)Ols.Status.DLL_INITIALIZE_ERROR:
                    throw new ApplicationException("WinRing DLL_INITIALIZE_ERROR");
            }

            // Check WinRing0 status
            switch (mOls.GetDllStatus())
            {
                case (uint)Ols.OlsDllStatus.OLS_DLL_NO_ERROR:
                    break;
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_LOADED:
                    throw new ApplicationException("WinRing OLS_DRIVER_NOT_LOADED");
                case (uint)Ols.OlsDllStatus.OLS_DLL_UNSUPPORTED_PLATFORM:
                    throw new ApplicationException("WinRing OLS_UNSUPPORTED_PLATFORM");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_FOUND:
                    throw new ApplicationException("WinRing OLS_DLL_DRIVER_NOT_FOUND");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_UNLOADED:
                    throw new ApplicationException("WinRing OLS_DLL_DRIVER_UNLOADED");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK:
                    throw new ApplicationException("WinRing DRIVER_NOT_LOADED_ON_NETWORK");
                case (uint)Ols.OlsDllStatus.OLS_DLL_UNKNOWN_ERROR:
                    throw new ApplicationException("WinRing OLS_DLL_UNKNOWN_ERROR");
            }
        }

        private uint GetCpuInfo()
        {
            uint eax = 0, ebx = 0, ecx = 0, edx = 0;
            mOls.CpuidPx(0x00000000, ref eax, ref ebx, ref ecx, ref edx, (UIntPtr)1);
            if (mOls.CpuidPx(0x00000001, ref eax, ref ebx, ref ecx, ref edx, (UIntPtr)1) == 1)
            {
                return eax;
            }

            return 0;
        }

        internal virtual bool SmuWriteReg(uint addr, uint data, uint pciAddr)
        {
            // Clear response
            int res = mOls.WritePciConfigDwordEx(pciAddr, mSmu.SMU_OFFSET_ADDR, addr);
            if (res == 1)
            {
                res = mOls.WritePciConfigDwordEx(pciAddr, mSmu.SMU_OFFSET_DATA, data);
            }

            return res == 1;
        }

        internal virtual bool SmuReadReg(uint addr, ref uint data, uint pciAddr)
        {
            // Clear response
            int res = mOls.WritePciConfigDwordEx(pciAddr, mSmu.SMU_OFFSET_ADDR, addr);
            if (res == 1)
            {
                res = mOls.ReadPciConfigDwordEx(pciAddr, mSmu.SMU_OFFSET_DATA, ref data);
            }

            return res == 1;
        }

        internal virtual bool SmuWaitDone(uint pciAddr)
        {
            bool res = false;
            ushort timeout = 1000;
            uint data = 0;
            while ((!res || data != 1) && --timeout > 0)
            {
                res = SmuReadReg(mSmu.SMU_ADDR_RSP, ref data, pciAddr);
            }

            if (timeout == 0 || data != 1) res = false;

            return res;
        }

        internal virtual bool SmuWrite(uint msg, uint value, uint pciAddr)
        {
            // Mutex
            bool res = mMutexPci.WaitOne(5000);

            // Clear response
            if (res)
            {
                res = SmuWriteReg(mSmu.SMU_ADDR_RSP, 0, pciAddr);
            }

            if (res)
            {
                // Write data
                SmuWriteReg(mSmu.SMU_ADDR_ARG0, value, pciAddr);

                // Send message
                res = SmuWriteReg(mSmu.SMU_ADDR_MSG, msg, pciAddr);

                if (res)
                {
                    res = SmuWaitDone(pciAddr);
                }
            }

            mMutexPci.ReleaseMutex();

            return res;
        }

        internal virtual SMU.Status ApplySMUCommand(SMUCmdConstant command, uint value, uint pciAddr)
        {
            try
            {
                if (SmuWrite((uint)command, value, pciAddr))
                {
                    // Read response
                    uint data = 0;
                    if (SmuReadReg(mSmu.SMU_ADDR_RSP, ref data, pciAddr))
                    {
                        return (SMU.Status)data;
                    }

                    mLogger.Invoke("Error reading response!");
                }
                else
                {
                    mLogger.Invoke("Error on writing SMU!");
                }
            }
            catch (ApplicationException e)
            {
                mLogger.Invoke($"ERROR: {e.Message}");
            }

            return SMU.Status.FAILED;
        }
    }
}
