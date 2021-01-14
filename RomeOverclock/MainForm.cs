using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows.Forms;
using OpenLibSys;
using ZenStatesDebugTool;

namespace RomeOverclock
{
    public partial class MainForm : Form
    {

        private uint _coreCount;
        private string _cpuName;
        private readonly SMUCommand mSmuCmd;
        private readonly SettingManager mSettings;

        private Dictionary<string, Dictionary<string, Action>> _presetFunctions;

        private int CountDecimals(decimal x)
        {
            var precision = 0;

            while (x * (decimal)Math.Pow(10, precision) !=
                   Math.Round(x * (decimal)Math.Pow(10, precision)))
                precision++;
            return precision;
        }

        private void PopulateFrequencyList(ComboBox.ObjectCollection l)
        {
            for (var i = 1800; i <= 4000; i += 50)
            {
                var v = i / 1000.00;
                l.Add(new FrequencyListItem(i, $"{v:0.00} GHz"));
            }
        }

        private void PopulateCCDList(ComboBox.ObjectCollection l)
        {
            for (uint i = 0; i < _coreCount; i++)
            {
                l.Add(new CoreListItem(i / 8, i / 4, i));
            }
        }

        private void PopulateVoltageList(ComboBox.ObjectCollection l)
        {
            for (var i = 12; i < 128; i += 1)
            {
                var voltage = (decimal)(1.55 - i * 0.00625);
                int decimals = CountDecimals(voltage);

                if (decimals <= 3)
                {
                    l.Add(new VoltageListItem(i, (double)voltage));
                }
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

        private void SetButtonsEnabled(bool enabled)
        {
            applyAllBtn.Enabled = enabled;
            applyAllBtn.Enabled = enabled;
            revertAllBtn.Enabled = enabled;
            dualsocketCheck.Enabled = enabled;
        }

        private void SetFormsEnabled(bool enabled)
        {
            /*applyACBtn.Enabled = enabled;
            //applySCBtn.Enabled = enabled;
            //applyVoltageBtn.Enabled = enabled;
            revertACBtn.Enabled = enabled;

            AC_freqSelect.Enabled = enabled;
            SC_freqSelect.Enabled = enabled;
            SC_coreSelect.Enabled = enabled;
            dualsocketCheck.Enabled = enabled;
            fullPerfBtn.Enabled = enabled;
            applyLockBtn.Enabled = enabled;
            revertVoltageBtn.Enabled = enabled;
            presetApplyBtn.Enabled = enabled;*/
        }

        private void InitDefaultForm()
        {
            SetStatus("Loading...");

            PopulateFrequencyList(AC_freqSelect.Items);
            AC_freqSelect.SelectedIndex = 16;
            presetCpuSelect.SelectedIndex = 0;
            presetPresetSelect.SelectedIndex = 0;
            SetButtonsEnabled(true);
            SetFormsEnabled(false);

            voltageInp.Controls[0].Visible = false;

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

            mSettings = new SettingManager();

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

            // TODO: refactoring preset list
            _presetFunctions = new Dictionary<string, Dictionary<string, Action>>
            {
                {"64", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        mSettings.FreqLock = false;
                        mSettings.Voltage = SMUCommand.ToVoltageInteger(1.05m);
                        mSettings.AllCoreFreq = 3800;

                        mSettings.EDC = 30;
                        mSettings.TDC = 0;
                        mSettings.PPT = 0;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"Best of both", () =>
                    {
                        mSettings.FreqLock = true;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.05m): -1;
                        mSettings.AllCoreFreq = 3200;

                        mSettings.EDC = 600;
                        mSettings.TDC = 0;
                        mSettings.PPT = 0;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"High Single-core", () =>
                    {
                        mSettings.FreqLock = true;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.1m): -1;
                        mSettings.AllCoreFreq = 3400;

                        mSettings.EDC = 700;
                        mSettings.TDC = 700;
                        mSettings.PPT = 1500;
                        mSettings.ApplyChanges(mSmuCmd);
                    }}
                }},
                {"48", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        mSettings.FreqLock = true;
                        mSettings.Voltage = SMUCommand.ToVoltageInteger(1.05m);
                        mSettings.AllCoreFreq = 3800;

                        mSettings.EDC = 45;
                        mSettings.TDC = 0;
                        mSettings.PPT = 0;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"Best of both", () =>
                    {
                        mSettings.FreqLock = false;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.05m): -1;
                        mSettings.AllCoreFreq = 3300;

                        mSettings.EDC = 600;
                        mSettings.TDC = 0;
                        mSettings.PPT = 0;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"High Single-core", () =>
                    {
                        mSettings.FreqLock = false;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.1m): -1;
                        mSettings.AllCoreFreq = 3500;

                        mSettings.EDC = 700;
                        mSettings.TDC = 700;
                        mSettings.PPT = 1500;
                        mSettings.ApplyChanges(mSmuCmd);
                    }}
                }},
                {"32", new Dictionary<string, Action>
                {
                    {"High Multi-core", () =>
                    {
                        mSettings.FreqLock = true;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.05m): -1;
                        mSettings.AllCoreFreq = 3450;

                        mSettings.EDC = 600;
                        mSettings.TDC = 600;
                        mSettings.PPT = 1500;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"Best of both", () =>
                    {
                        mSettings.FreqLock = false;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.05m): -1;
                        mSettings.AllCoreFreq = 3300;

                        mSettings.EDC = 600;
                        mSettings.TDC = 0;
                        mSettings.PPT = 0;
                        mSettings.ApplyChanges(mSmuCmd);
                    }},
                    {"High Single-core", () =>
                    {
                        mSettings.FreqLock = false;
                        mSettings.Voltage = _cpuName.StartsWith("2S1")? SMUCommand.ToVoltageInteger(1.1m): -1;
                        mSettings.AllCoreFreq = 3500;

                        mSettings.EDC = 700;
                        mSettings.TDC = 700;
                        mSettings.PPT = 1500;
                        mSettings.ApplyChanges(mSmuCmd);
                    }}
                }}
            };
        }

        private void applyAllBtn_Click(object sender, EventArgs e)
        {
            mSettings.FreqLock = overclockCheck.Checked;
            mSettings.Voltage = SMUCommand.ToVoltageInteger(voltageInp.Value);
            mSettings.AllCoreFreq = ((FrequencyListItem)AC_freqSelect.SelectedItem).frequency;

            mSettings.EDC = Convert.ToInt32(EDC_inp.Value);
            mSettings.TDC = Convert.ToInt32(TDC_inp.Value);
            mSettings.PPT = Convert.ToInt32(PPT_inp.Value);
            mSettings.ApplyChanges(mSmuCmd);
        }

        private void revertAllBtn_Click(object sender, EventArgs e)
        {
            mSettings.LoadDefaultValues();
            mSettings.ApplyChanges(mSmuCmd);
        }

        private void presetApplyBtn_Click(object sender, EventArgs e)
        {
            var presetCPU = presetCpuSelect.Text;
            var presetPreset = presetPresetSelect.Text;
            SetStatus(presetCPU + " " + presetPreset);
            var cores = presetCPU.Substring(0, 2);

            _presetFunctions[cores][presetPreset]();
        }
    }
}