using Caff;
using Xunit;

namespace CaffTests;

// Exercises the real PowerCreateRequest/PowerSetRequest P/Invoke path
// in-process — creating power requests needs no elevation. (Observing them via
// `powercfg /requests` does need an elevated prompt and remains a manual check.)
// The requests created here leak by design and vanish when the test process exits.
public class PowerRequestTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HoldPowerRequest_ReturnsValidHandle(bool display, bool idle)
    {
        var opts = new Options { Display = display, Idle = idle };
        IntPtr request = Program.HoldPowerRequest(opts, "caff.Tests");
        Assert.NotEqual(new IntPtr(-1), request);
        Assert.NotEqual(IntPtr.Zero, request);
    }
}
