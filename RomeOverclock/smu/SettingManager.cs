using System;
using System.Collections.Generic;
using ZenStatesDebugTool;

namespace RomeOverclock
{
    using SettingDict = Dictionary<string, int>;

    public class SettingManager
    {
        public const int kDefaultIntValue = -1;

        private const string kKeyPPT = "PPT";
        private const string kKeyTDC = "TDC";
        private const string kKeyEDC = "EDC";
        private const string kKeyAllCoreFreq = "AllCoreFreq";
        private const string kKeyVoltage = "Voltage";
        private const string kKeyFreqLock = "FreqLock";
        private const string kKeyDualSocket = "DualSocket";
        private const string kKeyFullPerf = "FullPerf";

        private readonly SettingDict mSettings = new SettingDict();
        private readonly SettingDict mAppliedSettings = new SettingDict();

        public SettingManager()
        {
            LoadDefaultValues();
        }

        public int PPT
        {
            get { return GetInteger(mSettings, kKeyPPT); }
            set { SetInteger(mSettings, kKeyPPT, value); }
        }

        public int TDC
        {
            get { return GetInteger(mSettings, kKeyTDC); }
            set { SetInteger(mSettings, kKeyTDC, value); }
        }

        public int EDC
        {
            get { return GetInteger(mSettings, kKeyEDC); }
            set { SetInteger(mSettings, kKeyEDC, value); }
        }

        public bool FreqLock
        {
            get { return GetBoolean(mSettings, kKeyFreqLock); }
            set { SetBoolean(mSettings, kKeyFreqLock, value); }
        }

        public bool DualSocket
        {
            get { return GetBoolean(mSettings, kKeyDualSocket); }
            set { SetBoolean(mSettings, kKeyDualSocket, value); }
        }

        public int Voltage
        {
            get { return GetInteger(mSettings, kKeyVoltage); }
            set { SetInteger(mSettings, kKeyVoltage, value); }
        }

        public int AllCoreFreq
        {
            get { return GetInteger(mSettings, kKeyAllCoreFreq); }
            set { SetInteger(mSettings, kKeyAllCoreFreq, value); }
        }

        /**
         * DO NOT USE, COULD BE DANGEROUS
         */
        public bool FullPerf
        {
            get { return GetBoolean(mSettings, kKeyFullPerf); }
            set { SetBoolean(mSettings, kKeyFullPerf, value); }
        }

        public void LoadDefaultValues()
        {
            FreqLock = false;
            Voltage = -1;
            AllCoreFreq = -1;

            EDC = 700;
            TDC = 700;
            PPT = 1500;
        }

        public bool ApplyChanges(SMUCommand smuCmd)
        {
            smuCmd.isDualSocket = DualSocket;
            smuCmd.RevertVoltage();
            ApplySettingChange(kKeyVoltage, () =>
            {
                return Voltage == kDefaultIntValue || smuCmd.ApplyVoltage(Voltage);
            });

            ApplySettingChange(kKeyAllCoreFreq, () =>
            {
                if (AllCoreFreq != kDefaultIntValue)
                {
                    return smuCmd.ApplyFrequencyAllCoreSetting(Convert.ToUInt32(AllCoreFreq));
                }
                else
                {
                    return smuCmd.RevertFrequency();
                }
            });

            ApplySettingChange(kKeyFreqLock, () =>
            {
                return smuCmd.ApplyFreqLock(FreqLock);
            });

            ApplySettingChange(kKeyEDC, () =>
            {
                return smuCmd.ApplyEDCSetting(Convert.ToUInt32(EDC));
            });

            ApplySettingChange(kKeyTDC, () =>
            {
                return smuCmd.ApplyTDCSetting(Convert.ToUInt32(TDC));
            });

            ApplySettingChange(kKeyPPT, () =>
            {
                return smuCmd.ApplyPPTSetting(Convert.ToUInt32(PPT));
            });

            return true;
        }

        private static int GetInteger(SettingDict dict, string key) { return dict.ContainsKey(key) ? dict[key] : kDefaultIntValue; }
        private static void SetInteger(SettingDict dict, string key, int val) { dict[key] = val; }

        private static bool GetBoolean(SettingDict dict, string key)
        {
            // 1 = true, others = false
            return GetInteger(dict, key) == 1;
        }

        private static void SetBoolean(SettingDict dict, string key, bool val)
        {
            // 1 = true, others = false
            SetInteger(dict, key, val ? 1 : 0);
        }

        private bool HasChanged(string key)
        {
            return GetInteger(mSettings, key) != GetInteger(mAppliedSettings, key);
        }

        private void ApplySettingChange(string key, Func<bool> apply)
        {
            if (HasChanged(key))
            {
                if (apply.Invoke())
                {
                    mAppliedSettings[key] = GetInteger(mSettings, key);
                }
            }
        }
    }
}