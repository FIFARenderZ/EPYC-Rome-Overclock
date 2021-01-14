using System;
using System.Diagnostics.CodeAnalysis;
using Moq;
using Xunit;
using RomeOverclock;
using ZenStatesDebugTool;

namespace RomeOverclockUnitTest
{
    [ExcludeFromCodeCoverage]
    public class SettingManagerUnitTest
    {
        private readonly Mock<SMUCommand> mMockSmuCommand;

        public SettingManagerUnitTest()
        {
            mMockSmuCommand = new Mock<SMUCommand>(
                (new Mock<SMUCommand.OlsInterface>()).Object,
                false,
                (new Mock<SMUCommand.LoggerDelegate>()).Object);
            mMockSmuCommand.CallBase = true;
            mMockSmuCommand.Setup(mock => mock.ApplyCommandImpl(
                    It.IsAny<SMUCommand.SMUCmdConstant>(),
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);
        }

        [Theory]
        [InlineData("AllCoreFreq")]
        [InlineData("Voltage")]
        [InlineData("PPT")]
        [InlineData("TDC")]
        [InlineData("EDC")]
        public void ApplySettingChange_will_apply_change(string key)
        {
            var subject = getMockSubject();
            subject.Object.ApplySettingChange(key, () => { return false; });
            subject.Object.mSettings[key] = 1;
            subject.Verify(mock => mock.HasChanged(key), Times.Once());
            Assert.False(subject.Object.mAppliedSettings.ContainsKey(key));

            subject.Reset();
            subject.Object.ApplySettingChange(key, () => { return true; });
            subject.Verify(mock => mock.HasChanged(key), Times.Once());
            Assert.True(subject.Object.mAppliedSettings.ContainsKey(key));
            Assert.Equal(1, subject.Object.mAppliedSettings[key]);

            subject.Reset();
            uint callCount = 0;
            subject.Object.ApplySettingChange(key, () =>
            {
                ++callCount;
                return true;
            });
            Assert.Equal(0u, callCount);
            subject.Verify(mock => mock.HasChanged(key), Times.Once());
            Assert.True(subject.Object.mAppliedSettings.ContainsKey(key));
            Assert.Equal(1, subject.Object.mAppliedSettings[key]);
        }

        [Fact]
        public void ApplyChanges_apply_dafault_settings()
        {
            var subject = getMockSubject();
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));

            mMockSmuCommand.Verify(mock => mock.ApplyVoltage(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>()),
                Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyFrequencyAllCoreSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyFreqLock(false), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyEDCSetting(700), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyTDCSetting(700), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyPPTSetting(1500), Times.Once());
        }

        [Fact]
        public void ApplyChanges_will_only_apply_changed_settings()
        {
            var subject = getMockSubject();
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));

            mMockSmuCommand.Invocations.Clear();
            var voltage = SMUCommand.ToVoltageInteger(1.05m);
            subject.Object.Voltage = voltage;
            subject.Object.AllCoreFreq = 3400;
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));
            mMockSmuCommand.Verify(mock => mock.RevertVoltage(), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyVoltage(
                    voltage,
                    It.IsAny<int>(),
                    It.IsAny<int>()),
                Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyFrequencyAllCoreSetting(3400), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyFreqLock(It.IsAny<bool>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyEDCSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyTDCSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyPPTSetting(It.IsAny<uint>()), Times.Never());

            mMockSmuCommand.Invocations.Clear();
            subject.Object.PPT = 1400;
            subject.Object.EDC = 600;
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));
            mMockSmuCommand.Verify(mock => mock.RevertVoltage(), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyVoltage(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>()),
                Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyFrequencyAllCoreSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyFreqLock(It.IsAny<bool>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyEDCSetting(600), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyTDCSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyPPTSetting(1400), Times.Once());
        }

        private Mock<SettingManager> getMockSubject()
        {
            var subject = new Mock<SettingManager>();
            subject.CallBase = true;
            return subject;
        }
    }
}
