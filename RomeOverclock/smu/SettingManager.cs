using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ZenStatesDebugTool;

[assembly: InternalsVisibleTo("RomeOverclockUnittest"),
    InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace RomeOverclock
{
    using SettingDict = Dictionary<string, int>;

    public class SettingManager
    {
        public class Settings : INotifyPropertyChanged
        {
            private const int kDefaultIntValue = -1;
            private const string kKeyPPT = "PPT";
            private const string kKeyTDC = "TDC";
            private const string kKeyEDC = "EDC";
            private const string kKeyAllCoreFreq = "AllCoreFreq";
            private const string kKeyVoltage = "Voltage";
            private const string kKeyFreqLock = "FreqLock";
            private const string kKeyDualSocket = "DualSocket";

            private readonly SettingDict mSettingDict = new SettingDict();

            public event PropertyChangedEventHandler PropertyChanged;

            public int PPT
            {
                get => GetInteger(mSettingDict, kKeyPPT);
                set => SetInteger(mSettingDict, kKeyPPT, value, _ => NotifyPropertyChanged());
            }

            public int TDC
            {
                get => GetInteger(mSettingDict, kKeyTDC);
                set => SetInteger(mSettingDict, kKeyTDC, value, _ => NotifyPropertyChanged());
            }

            public int EDC
            {
                get => GetInteger(mSettingDict, kKeyEDC);
                set => SetInteger(mSettingDict, kKeyEDC, value, _ => NotifyPropertyChanged());
            }

            public bool FreqLock
            {
                get => GetBoolean(mSettingDict, kKeyFreqLock);
                set => SetBoolean(mSettingDict, kKeyFreqLock, value, _ => NotifyPropertyChanged());
            }

            public bool DualSocket
            {
                get => GetBoolean(mSettingDict, kKeyDualSocket);
                set => SetBoolean(mSettingDict, kKeyDualSocket, value, _ => NotifyPropertyChanged());
            }

            public int Voltage
            {
                get => GetInteger(mSettingDict, kKeyVoltage);
                set => SetInteger(mSettingDict, kKeyVoltage, value, _ => NotifyPropertyChanged());
            }

            public decimal VoltageReal
            {
                get => SMUCommand.ToVoltageDecimal(GetInteger(mSettingDict, kKeyVoltage));
                set => SetInteger(mSettingDict, kKeyVoltage,
                    SMUCommand.ToVoltageInteger(value), _ => NotifyPropertyChanged());
            }

            public int AllCoreFreq
            {
                get => GetInteger(mSettingDict, kKeyAllCoreFreq);
                set => SetInteger(mSettingDict, kKeyAllCoreFreq, value, _ => NotifyPropertyChanged());
            }

            public void LoadDefaultValues()
            {
                FreqLock = false;
                Voltage = -1;
                AllCoreFreq = 2600;

                EDC = 700;
                TDC = 700;
                PPT = 1500;
            }

            internal int this[string key]
            {
                get => mSettingDict[key];
                set => mSettingDict[key] = value;
            }

            protected internal static bool IsDefaultValue(int val)
            {
                return val == kDefaultIntValue;
            }

            private static int GetInteger(SettingDict dict, string key) { return dict.ContainsKey(key) ? dict[key] : kDefaultIntValue; }
            private static void SetInteger(SettingDict dict, string key, int val, Action<int> notifyChange)
            {
                if (GetInteger(dict, key) != val)
                {
                    dict[key] = val;
                    notifyChange?.Invoke(val);
                }
            }

            private static bool GetBoolean(SettingDict dict, string key)
            {
                // 1 = true, others = false
                return GetInteger(dict, key) == 1;
            }

            private static void SetBoolean(SettingDict dict, string key, bool val, Action<bool> notifyChange)
            {
                // 1 = true, others = false
                SetInteger(dict, key, val ? 1 : 0, _ => notifyChange?.Invoke(val));
            }

            internal bool SerializeToJson(string fileName)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                };

                var jsonString = JsonSerializer.Serialize(mSettingDict, options);

                try
                {
                    using (var sw = new StreamWriter(fileName, false, new UTF8Encoding()))
                    {
                        sw.Write(jsonString);
                    }
                }
                catch (Exception)
                {
                    return false;
                }

                return true;
            }

            internal bool DeserializeFromJson(string fileName)
            {
                try
                {
                    string jsonString;
                    using (var sr = new StreamReader(fileName, new UTF8Encoding()))
                    {
                        jsonString = sr.ReadToEnd();
                    }

                    var dict = JsonSerializer.Deserialize<SettingDict>(jsonString);
                    foreach (KeyValuePair<string, int> entry in dict)
                    {
                        mSettingDict[entry.Key] = entry.Value;
                    }
                }
                catch (Exception)
                {
                    return false;
                }

                return true;
            }

            internal virtual bool PropertyHasChanged(Settings other, PropertyInfo prop)
            {
                return !prop.GetValue(this).Equals(prop.GetValue(other));
            }

            internal virtual void ApplySettingChange<T>(Settings other, Expression<Func<Settings, T>> property, Func<T, bool> apply)
            {
                var prop = (PropertyInfo)((MemberExpression)property.Body).Member;
                if (PropertyHasChanged(other, prop))
                {
                    var newVal = (T)prop.GetValue(other);
                    if (apply.Invoke(newVal))
                    {
                        prop.SetValue(this, prop.GetValue(other), null);
                    }
                }
            }
            private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public Settings OnGoingSettings { get => mSettings; }
        internal Settings RunningSettings { get => mRunningSettings; }

        private readonly Settings mSettings = new Settings();
        private readonly Settings mRunningSettings = new Settings();
        private readonly string mSettingFileName;

        public SettingManager(string fileName)
        {
            mSettingFileName = fileName;
            mSettings.LoadDefaultValues();
            if (!mSettings.DeserializeFromJson(fileName))
            {
                SaveSettingToDisk();
            }
        }

        public bool ApplyChanges(SMUCommand smuCmd)
        {
            mRunningSettings.DualSocket = mSettings.DualSocket;
            smuCmd.IsDualSocket = mSettings.DualSocket;
            ApplySettingChange(x => x.Voltage, (val) =>
            {
                smuCmd.RevertVoltage();
                return Settings.IsDefaultValue(val) || smuCmd.ApplyVoltage(val);
            });

            ApplySettingChange(x => x.AllCoreFreq, (val) =>
            {
                if (Settings.IsDefaultValue(val))
                {
                    return smuCmd.RevertFrequency();
                }
                else
                {
                    return smuCmd.ApplyFrequencyAllCoreSetting(Convert.ToUInt32(val));
                }
            });

            ApplySettingChange(x => x.FreqLock, (val) =>
            {
                return smuCmd.ApplyFreqLock(val);
            });

            ApplySettingChange(x => x.EDC, (val) =>
            {
                return smuCmd.ApplyEDCSetting(Convert.ToUInt32(val));
            });

            ApplySettingChange(x => x.TDC, (val) =>
            {
                return smuCmd.ApplyTDCSetting(Convert.ToUInt32(val));
            });

            ApplySettingChange(x => x.PPT, (val) =>
            {
                return smuCmd.ApplyPPTSetting(Convert.ToUInt32(val));
            });

            return true;
        }

        public bool SaveSettingToDisk()
        {
            return mRunningSettings.SerializeToJson(mSettingFileName);
        }

        internal virtual void ApplySettingChange<T>(Expression<Func<Settings, T>> property, Func<T, bool> apply)
        {
            mRunningSettings.ApplySettingChange(mSettings, property, apply);
        }
    }
}