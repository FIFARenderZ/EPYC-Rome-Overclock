using ZenStatesDebugTool;
using Moq;
using Xunit;
using System.Diagnostics.CodeAnalysis;

namespace RomeOverclockUnittest
{
    [ExcludeFromCodeCoverage]
    public class SMUUnitTest
    {
        private readonly Mock<SMUCommand.LoggerDelegate> mMockLogger = new Mock<SMUCommand.LoggerDelegate>();
        private readonly Mock<OpenLibSys.Ols> mMockOls = new Mock<OpenLibSys.Ols>();
        private readonly SMUCommand mSubject;

        public SMUUnitTest()
        {
            mMockOls.Object.WritePciConfigDwordEx = (uint _, uint __, uint ___) =>
                {
                    return 1;
                };
            mSubject = new SMUCommand(mMockOls.Object, false, mMockLogger.Object);
        }

        [Theory]
        [InlineData(0x03B10570, 0, 0)]
        [InlineData(0x03B10570, 0, 0x0000A000)]
        public void SmuWriteReg_will_clear_then_write_reg(uint addr, uint data, uint pciAddr)
        {
            Assert.True(mSubject.SmuWriteReg(addr, data, pciAddr));
        }

    }
}
