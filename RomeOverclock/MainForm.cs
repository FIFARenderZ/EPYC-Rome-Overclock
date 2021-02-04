using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ZenStatesDebugTool;

namespace RomeOverclock
{
    public partial class MainForm : Form, INotifyPropertyChanged
    {
        const string kSettingsFileName = "settings.json";

        public FrequencyListItem SelectedFrequencyItem
        {
            get
            {
                return AC_freqSelect.Items.Cast<FrequencyListItem>()
                    .SingleOrDefault(i => i.Frequency == mSettings.OnGoingSettings.AllCoreFreq);
            }
            set
            {
                if (mSettings.OnGoingSettings.AllCoreFreq != value.Frequency)
                {
                    mSettings.OnGoingSettings.AllCoreFreq = value.Frequency;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly uint _coreCount;
        private readonly string _cpuName;
        private readonly SMUCommand mSmuCmd;
        private readonly SettingManager mSettings;
        private readonly Dictionary<string, Dictionary<string, Action>> _presetFunctions;

        private void PopulateFrequencyList(ComboBox.ObjectCollection l)
        {
            for (var i = 1800; i <= 4000; i += 50)
            {
                var v = i / 1000.00;
                l.Add(new FrequencyListItem(i, $"{v:0.00} GHz"));
            }
        }

        private void SetStatus(string status)
        {
            statusLabel.Text = status;
            logBox.Text = $@"{status}{Environment.NewLine}" + logBox.Text;
        }

        private void HandleError(Exception ex, string title = "Error")
        {
            SetStatus("ERROR!");
            MessageBox.Show(ex.Message, title);
        }

        private void EnableControls(bool enabled)
        {
            applyAllBtn.Enabled = enabled;
            revertAllBtn.Enabled = enabled;
            saveBtn.Enabled = enabled;
            dualsocketCheck.Enabled = enabled;
        }

        private void InitDefaultForm()
        {
            SetStatus("Loading...");

            PopulateFrequencyList(AC_freqSelect.Items);
            presetCpuSelect.SelectedIndex = 0;
            presetPresetSelect.SelectedIndex = 0;
            EnableControls(true);

            if (voltageInp.Controls.Count > 0)
            {
                voltageInp.Controls[0].Visible = false;
            }

            SetStatus("Ready.");
        }


        public MainForm()
        {
            InitializeComponent();

            try
            {
                mSmuCmd = new SMUCommand(false, (string msg) =>
                {
                    SetStatus(msg);
                });
            }
            catch (ApplicationException ex)
            {
                MessageBox.Show(ex.Message, @"Error");
                Dispose();
                Application.Exit();
            }

            mSettings = new SettingManager(kSettingsFileName);

            mainFormBindingSource.DataSource = this;
            settingsBindingSource.DataSource = mSettings.OnGoingSettings;

#if !DEBUG
            var CPUID = mSmuCmd.CPUID;
            if (CPUID != 0x00830F00 && CPUID != 0x00830F10) // EPYC Rome ES
            {
                MessageBox.Show(@"CPU not supported!", @"Error");
                Dispose();
                Application.Exit();
                return;
            }
#endif
#if DEBUG
            var res = MessageBox.Show(
                @"This is an experimental version of this software. Keep in mind that it is possible to encounter bugs.",
                @"Warning", MessageBoxButtons.OKCancel);
            if (res != DialogResult.OK)
            {
                Dispose();
                Application.Exit();
            }
#endif

            var mg = new ManagementObjectSearcher("Select * from Win32_Processor").Get()
                .Cast<ManagementBaseObject>();
            _coreCount = Convert.ToUInt32(mg.Sum(item => int.Parse(item["NumberOfCores"].ToString())));
            _cpuName = (string)mg.First()["Name"];
            _cpuName = _cpuName.Replace("AMD Eng Sample: ", "").Trim();

            InitDefaultForm();

            FrequencyListItem findFreqItemByFreq(int freq)
            {
                return AC_freqSelect.Items.Cast<FrequencyListItem>().SingleOrDefault(i => i.Frequency == freq);
            }

            // TODO: refactoring preset list use data binding
            var settings = mSettings.OnGoingSettings;
            _presetFunctions = new Dictionary<string, Dictionary<string, Action>>
            {
                {"64", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        settings.FreqLock = false;
                        settings.VoltageReal = 1.05m;
                        SelectedFrequencyItem = findFreqItemByFreq(3800);

                        settings.EDC = 30;
                        settings.TDC = 0;
                        settings.PPT = 0;
                    }},
                    {"Best of both", () =>
                    {
                        settings.FreqLock = true;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.05m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3200);

                        settings.EDC = 600;
                        settings.TDC = 0;
                        settings.PPT = 0;
                    }},
                    {"High Single-core", () =>
                    {
                        settings.FreqLock = true;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.1m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3400);

                        settings.EDC = 700;
                        settings.TDC = 700;
                        settings.PPT = 1500;
                    }}
                }},
                {"48", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        settings.FreqLock = true;
                        settings.VoltageReal = 1.05m;
                        SelectedFrequencyItem = findFreqItemByFreq(3800);

                        settings.EDC = 45;
                        settings.TDC = 0;
                        settings.PPT = 0;
                    }},
                    {"Best of both", () =>
                    {
                        settings.FreqLock = false;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.05m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3300);

                        settings.EDC = 600;
                        settings.TDC = 0;
                        settings.PPT = 0;
                    }},
                    {"High Single-core", () =>
                    {
                        settings.FreqLock = false;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.1m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3500);

                        settings.EDC = 700;
                        settings.TDC = 700;
                        settings.PPT = 1500;
                    }}
                }},
                {"32", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        settings.FreqLock = true;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.05m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3450);

                        settings.EDC = 600;
                        settings.TDC = 600;
                        settings.PPT = 1500;
                    }},
                    {"Best of both", () =>
                    {
                        settings.FreqLock = false;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.05m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3300);

                        settings.EDC = 600;
                        settings.TDC = 0;
                        settings.PPT = 0;
                    }},
                    {"High Single-core", () =>
                    {
                        settings.FreqLock = false;
                        if (_cpuName.StartsWith("2S1")) {
                            settings.VoltageReal = 1.1m;
                        } else {
                            settings.Voltage = -1;
                        }
                        SelectedFrequencyItem = findFreqItemByFreq(3500);

                        settings.EDC = 700;
                        settings.TDC = 700;
                        settings.PPT = 1500;
                    }}
                }}
            };
        }

        private void applyAllBtn_Click(object sender, EventArgs e)
        {
            mSettings.ApplyChanges(mSmuCmd);
        }

        private void saveBtn_Click(object sender, EventArgs e)
        {
            mSettings.SaveSettingToDisk();
        }

        private void revertAllBtn_Click(object sender, EventArgs e)
        {
            mSettings.OnGoingSettings.LoadDefaultValues();
            mSettings.ApplyChanges(mSmuCmd);
        }

        private void presetApplyBtn_Click(object sender, EventArgs e)
        {
            var presetCPU = presetCpuSelect.Text;
            var presetPreset = presetPresetSelect.Text;
            if (string.IsNullOrWhiteSpace(presetCPU) || string.IsNullOrWhiteSpace(presetPreset))
            {
                return;
            }

            SetStatus(presetCPU + " " + presetPreset);
            var cores = presetCPU.Substring(0, 2);

            _presetFunctions[cores][presetPreset]();
        }

        // TODO: unifying this notify property operations
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}