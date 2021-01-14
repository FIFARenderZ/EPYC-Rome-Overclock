using ZenStatesDebugTool;
using Moq;
using Xunit;
using System;
using System.Diagnostics.CodeAnalysis;
using static ZenStatesDebugTool.SMUCommand;

namespace RomeOverclockUnitTest
{
    [ExcludeFromCodeCoverage]
    public class SMUUnitTest
    {
        private const uint kReadDword = 0xF0F00F0F;
        private const uint kPCI_ADDR_0 = 0x0;
        private const uint kPCI_ADDR_1 = 0xA000;
        private const uint kADDR_RSP = 0x03B10570;
        private const uint kADDR_MSG = 0x03B10524;
        private const uint kADDR_ARG0 = 0x03B10A40;

        delegate void ReadPciConfigCallbackType(uint pciAddress, uint regAddress, ref uint value);
        delegate void SmuReadRegCallbackType(uint addr, ref uint data, uint pciAddr);

        private readonly Mock<SMUCommand.LoggerDelegate> mMockLogger = new Mock<SMUCommand.LoggerDelegate>();
        private readonly Mock<SMUCommand.OlsInterface> mMockOls = new Mock<SMUCommand.OlsInterface>();
        private readonly SMUCommand mSubject;


        public SMUUnitTest()
        {
            mMockOls.Setup(mock => mock.WritePciConfigDwordEx(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Returns(1);
            mMockOls.Setup(mock => mock.ReadPciConfigDwordEx(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    ref It.Ref<uint>.IsAny))
                .Callback((ReadPciConfigCallbackType)((uint _, uint __, ref uint value) => { value = kReadDword; }))
            .Returns(1);
            mSubject = new SMUCommand(mMockOls.Object, false, mMockLogger.Object);
        }

        [Theory]
        [InlineData(kADDR_RSP, 0, kPCI_ADDR_0)]
        [InlineData(kADDR_RSP, 0, kPCI_ADDR_1)]
        public void SmuWriteReg_will_clear_then_write_reg(uint addr, uint data, uint pciAddr)
        {
            Assert.True(mSubject.SmuWriteReg(addr, data, pciAddr));
            mMockOls.Verify(mock => mock.WritePciConfigDwordEx(pciAddr, 0xB8, addr), Times.Once());
            mMockOls.Verify(mock => mock.WritePciConfigDwordEx(pciAddr, 0xBC, data), Times.Once());
        }

        [Theory]
        [InlineData(kADDR_RSP, kPCI_ADDR_0)]
        [InlineData(kADDR_RSP, kPCI_ADDR_1)]
        public void SmuReadReg_will_clear_then_write_reg(uint addr, uint pciAddr)
        {
            uint data = 0;
            Assert.True(mSubject.SmuReadReg(addr, ref data, pciAddr));
            mMockOls.Verify(mock => mock.WritePciConfigDwordEx(pciAddr, 0xB8, addr), Times.Once());
            mMockOls.Verify(mock => mock.ReadPciConfigDwordEx(pciAddr, 0xBC, ref data), Times.Once());
            Assert.Equal(kReadDword, data);
        }

        [Fact]
        public void Dispose_will_call_dispose()
        {
            mSubject.Dispose();
            mMockOls.Verify(mock => mock.Dispose(), Times.Once());
        }

        [Theory]
        [InlineData(kPCI_ADDR_0, 3)]
        [InlineData(kPCI_ADDR_1, 100)]
        public void SmuWaitDone_will_retry_until_done(uint pciAddr, uint retryCount)
        {
            var subject = getMockSubject();
            uint callCount = 0;
            subject.Setup(mock => mock.SmuReadReg(
                    kADDR_RSP,
                    ref It.Ref<uint>.IsAny,
                    pciAddr))
                .Callback((SmuReadRegCallbackType)((uint _, ref uint data, uint __) =>
                {
                    data = callCount < retryCount ? 0u : 1u;
                    ++callCount;
                }))
                .Returns(true);

            subject.Object.SmuWaitDone(pciAddr);
            subject.Verify(mock => mock.SmuReadReg(
                        kADDR_RSP,
                        ref It.Ref<uint>.IsAny,
                        pciAddr),
                    Times.Exactly((int)retryCount + 1));
        }

        [Theory]
        [InlineData(0x53, 1500 * 1000, kPCI_ADDR_0)]
        [InlineData(0x18, 3400, kPCI_ADDR_1)]
        public void SmuWrite_will_clear_write_and_wait_done(uint msg, uint value, uint pciAddr)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.SmuWriteReg(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Returns(true);
            subject.Setup(mock => mock.SmuWaitDone(pciAddr))
                .Returns(true);

            Assert.True(subject.Object.SmuWrite(msg, value, pciAddr));
            subject.Verify(mock => mock.SmuWriteReg(kADDR_RSP, 0, pciAddr), Times.Once());
            subject.Verify(mock => mock.SmuWriteReg(kADDR_ARG0, value, pciAddr), Times.Once());
            subject.Verify(mock => mock.SmuWriteReg(kADDR_MSG, msg, pciAddr), Times.Once());
            subject.Verify(mock => mock.SmuWaitDone(pciAddr), Times.Once());
        }

        [Theory]
        [InlineData(SMUCmdConstant.kSetAllCoreFreq, 3400, kPCI_ADDR_0)]
        [InlineData(SMUCmdConstant.kSetPPT, 1500 * 1000, kPCI_ADDR_1)]
        [InlineData(SMUCmdConstant.kSetTDC, 700 * 1000, kPCI_ADDR_0)]
        [InlineData(SMUCmdConstant.kSetEDC, 700 * 1000, kPCI_ADDR_1)]
        public void ApplySMUCommand_will_apply_command_and_wait_response(SMUCmdConstant cmd, uint val, uint pciAddr)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.SmuWrite(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Returns(true);
            subject.Setup(mock => mock.SmuReadReg(
                    kADDR_RSP,
                    ref It.Ref<uint>.IsAny,
                    pciAddr))
                .Callback((SmuReadRegCallbackType)((uint _, ref uint data, uint __) => data = 1))
                .Returns(true);

            Assert.Equal(SMU.Status.OK, subject.Object.ApplySMUCommand(cmd, val, pciAddr));
            subject.Verify(mock => mock.SmuWrite((uint)cmd, val, pciAddr), Times.Once());
            subject.Verify(mock => mock.SmuReadReg(kADDR_RSP, ref It.Ref<uint>.IsAny, pciAddr), Times.Once());
        }

        [Theory]
        [InlineData(SMUCmdConstant.kSetAllCoreFreq, 3400, true)]
        [InlineData(SMUCmdConstant.kSetPPT, 1500 * 1000, true)]
        [InlineData(SMUCmdConstant.kSetTDC, 700 * 1000, true)]
        [InlineData(SMUCmdConstant.kSetEDC, 700 * 1000, true)]
        [InlineData(SMUCmdConstant.kSetAllCoreFreq, 3400, false)]
        [InlineData(SMUCmdConstant.kSetPPT, 1500 * 1000, false)]
        [InlineData(SMUCmdConstant.kSetTDC, 700 * 1000, false)]
        [InlineData(SMUCmdConstant.kSetEDC, 700 * 1000, false)]
        public void ApplyCommandImpl_will_apply_command_for_all_cpu(SMUCmdConstant cmd, uint val, bool isDualSocket)
        {
            var subject = getMockSubject(isDualSocket);
            Assert.Equal(isDualSocket, subject.Object.isDualSocket);

            subject.Setup(mock => mock.ApplySMUCommand(
                    It.IsAny<SMUCmdConstant>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Returns(SMU.Status.OK);

            Assert.True(subject.Object.ApplyCommandImpl(cmd, val, (uint value) =>
            {
                Assert.Equal(val, value);
                return "";
            }));

            subject.Verify(mock => mock.ApplySMUCommand(cmd, val, kPCI_ADDR_0), Times.Once());
            if (isDualSocket)
            {
                subject.Verify(mock => mock.ApplySMUCommand(cmd, val, kPCI_ADDR_1), Times.Once());
                mMockLogger.Verify(mock => mock.Invoke(It.IsAny<string>()), Times.Exactly(2));
            }
            else
            {
                mMockLogger.Verify(mock => mock.Invoke(It.IsAny<string>()), Times.Exactly(1));
            }
        }

        [Theory]
        [InlineData(3200)]
        [InlineData(3300)]
        [InlineData(3400)]
        [InlineData(3800)]
        public void ApplyFrequencyAllCoreSetting_will_apply_freq(uint freq)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x18,
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyFrequencyAllCoreSetting(freq));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x18,
                    freq,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1500)]
        public void ApplyPPTSetting_will_apply_ppt(uint ppt)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x53,
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyPPTSetting(ppt));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x53,
                    ppt * 1000,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(700)]
        public void ApplyTDCSetting_will_apply_tdc(uint tdc)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x54,
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyTDCSetting(tdc));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x54,
                    tdc * 1000,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(700)]
        public void ApplyEDCSetting_will_apply_edc(uint edc)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x55,
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyEDCSetting(edc));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x55,
                    edc * 1000,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Theory]
        [InlineData(12)]   // min
        [InlineData(72)]   // 1.1v
        [InlineData(80)]   // 1.05v
        [InlineData(128)]  // max
        public void ApplyVoltage_will_apply_voltage(int voltage)
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x12,
                    It.IsAny<uint>(),
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyVoltage(voltage));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x12,
                    (uint)voltage,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ApplyFreqLock_will_apply_freq_lock(bool isLocked)
        {
            var subject = getMockSubject();
            var cmd = (SMUCmdConstant)(isLocked ? 0x24 : 0x25);
            subject.Setup(mock => mock.ApplyCommandImpl(
                    cmd,
                    1,
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.ApplyFreqLock(isLocked));
            subject.Verify(mock => mock.ApplyCommandImpl(
                    cmd,
                    1,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Fact]
        public void RevertVoltage_will_revert_voltage()
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x13,
                    1,
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.RevertVoltage());
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x13,
                    1,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        [Fact]
        public void RevertFrequency_will_revert_freq()
        {
            var subject = getMockSubject();
            subject.Setup(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x19,
                    1,
                    It.IsAny<Func<uint, string>>()))
                .Returns(true);

            Assert.True(subject.Object.RevertFrequency());
            subject.Verify(mock => mock.ApplyCommandImpl(
                    (SMUCmdConstant)0x19,
                    1,
                    It.IsAny<Func<uint, string>>()),
                Times.Once());
        }

        private Mock<SMUCommand> getMockSubject(bool isSualSocket = false)
        {
            var subject = new Mock<SMUCommand>(mMockOls.Object, isSualSocket, mMockLogger.Object);
            subject.CallBase = true;
            return subject;
        }
    }
}
