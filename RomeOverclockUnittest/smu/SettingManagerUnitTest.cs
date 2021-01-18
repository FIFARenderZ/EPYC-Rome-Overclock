using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Moq;
using Xunit;
using RomeOverclock;
using ZenStatesDebugTool;
using static RomeOverclock.SettingManager;

namespace RomeOverclockUnitTest
{
    [ExcludeFromCodeCoverage]
    public class SettingManagerUnitTest
    {
        private readonly Mock<SMUCommand> mMockSmuCommand;

        public SettingManagerUnitTest()
        {
            mMockSmuCommand = new Mock<SMUCommand>(
                (new Mock<SMUCommand.IOlsInterface>()).Object,
                false,
                (new Mock<SMUCommand.LoggerDelegate>()).Object)
            {
                CallBase = true
            };
            mMockSmuCommand.Setup(mock => mock.ApplyCommandImpl(
                    It.IsAny<SMUCommand.SMUCmdConstant>(),
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);
        }

        public static TheoryData<Expression<Func<Settings, int>>> sApplyingSettingTestData = new TheoryData<Expression<Func<Settings, int>>>() {
            x => x.AllCoreFreq,
            x => x.Voltage,
            x => x.PPT,
            x => x.TDC,
            x => x.EDC,
        };

        [Theory]
        [MemberData(nameof(sApplyingSettingTestData))]
        public void ApplySettingChange_will_apply_change(Expression<Func<Settings, int>> property)
        {
            static Mock<Settings> getMockSetting()
            {
                return new Mock<Settings>()
                {
                    CallBase = true
                };
            }

            var subject = getMockSetting();
            var otherSubject = getMockSetting();
            otherSubject.Object.LoadDefaultValues();
            var prop = (PropertyInfo)((MemberExpression)property.Body).Member;

            subject.Object.ApplySettingChange(otherSubject.Object, property, _ => false);
            subject.Verify(mock => mock.PropertyHasChanged(otherSubject.Object, prop), Times.Once());

            subject.Invocations.Clear();
            uint callCount = 0;
            prop.SetValue(otherSubject.Object, 2, null);
            subject.Object.ApplySettingChange(otherSubject.Object, property, (val) =>
            {
                Assert.Equal(2, val);
                ++callCount;
                return true;
            });
            subject.Verify(mock => mock.PropertyHasChanged(otherSubject.Object, prop), Times.Once());
            Assert.Equal(1u, callCount);
            Assert.Equal(2, prop.GetValue(subject.Object));

            subject.Invocations.Clear();
            callCount = 0;
            subject.Object.ApplySettingChange(otherSubject.Object, property, (_) =>
            {
                ++callCount;
                return true;
            });
            Assert.Equal(0u, callCount);
            subject.Verify(mock => mock.PropertyHasChanged(otherSubject.Object, prop), Times.Once());
            Assert.Equal(2, prop.GetValue(subject.Object));
        }

        [Fact]
        public void ApplyChanges_apply_dafault_settings()
        {
            var subject = GetMockSubject();
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));

            mMockSmuCommand.Verify(mock => mock.ApplyVoltage(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>()),
                Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyFrequencyAllCoreSetting(2600), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyFreqLock(false), Times.Never());  // The default config of freq lock is disabled.
            mMockSmuCommand.Verify(mock => mock.ApplyEDCSetting(700), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyTDCSetting(700), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyPPTSetting(1500), Times.Once());
        }

        [Fact]
        public void ApplyChanges_will_only_apply_changed_settings()
        {
            var subject = GetMockSubject();
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));

            mMockSmuCommand.Invocations.Clear();
            subject.Object.OnGoingSettings.VoltageReal = 1.05m;
            subject.Object.OnGoingSettings.AllCoreFreq = 3400;
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));
            mMockSmuCommand.Verify(mock => mock.RevertVoltage(), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyVoltage(
                    SMUCommand.ToVoltageInteger(1.05m),
                    It.IsAny<int>(),
                    It.IsAny<int>()),
                Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyFrequencyAllCoreSetting(3400), Times.Once());
            mMockSmuCommand.Verify(mock => mock.ApplyFreqLock(It.IsAny<bool>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyEDCSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyTDCSetting(It.IsAny<uint>()), Times.Never());
            mMockSmuCommand.Verify(mock => mock.ApplyPPTSetting(It.IsAny<uint>()), Times.Never());

            mMockSmuCommand.Invocations.Clear();
            subject.Object.OnGoingSettings.PPT = 1400;
            subject.Object.OnGoingSettings.EDC = 600;
            Assert.True(subject.Object.ApplyChanges(mMockSmuCommand.Object));
            mMockSmuCommand.Verify(mock => mock.RevertVoltage(), Times.Never());
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

        private Mock<SettingManager> GetMockSubject()
        {
            return new Mock<SettingManager>("")
            {
                CallBase = true
            };
        }
    }
}
