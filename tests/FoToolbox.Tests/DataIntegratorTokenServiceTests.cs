using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using Xunit;

namespace FoToolbox.Tests;

public class DataIntegratorTokenServiceTests
{
    private sealed class FakeAcquirer : IDataIntegratorTokenAcquirer
    {
        public int Calls;
        public DualWriteToken Next = new("t1", null, new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        public Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct)
        { Calls++; return Task.FromResult(Next); }
    }

    private static DataIntegratorCredential Cred() => new("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", "svc@contoso.com", "pw");

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetToken_AcquiresThenCachesUntilExpiry()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var acquirer = new FakeAcquirer { Next = new DualWriteToken("acc", null, now.AddHours(1)) };
        var svc = new DataIntegratorTokenService(acquirer) { Clock = () => now };

        var a = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);
        var b = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        Assert.Equal("acc", a);
        Assert.Equal("acc", b);
        Assert.Equal(1, acquirer.Calls); // cached; not re-acquired
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetToken_ReacquiresWhenExpired()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var acquirer = new FakeAcquirer { Next = new DualWriteToken("acc", null, now.AddMinutes(1)) };
        var svc = new DataIntegratorTokenService(acquirer) { Clock = () => now };
        await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        svc.Clock = () => now.AddMinutes(5); // past expiry (incl. margin)
        acquirer.Next = new DualWriteToken("acc2", null, now.AddHours(1));
        var c = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        Assert.Equal("acc2", c);
        Assert.Equal(2, acquirer.Calls);
    }
}
