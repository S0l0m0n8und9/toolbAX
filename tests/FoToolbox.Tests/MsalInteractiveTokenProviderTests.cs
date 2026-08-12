using FoToolbox.Core.Auth;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class MsalInteractiveTokenProviderTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    /// <summary>Minimal <see cref="IAccount"/> stand-in — only HomeAccountId drives account selection.</summary>
    private sealed class FakeAccount : IAccount
    {
        public FakeAccount(string objectId, string tenantId)
        {
            Username = $"{objectId}@example.com";
            HomeAccountId = new AccountId($"{objectId}.{tenantId}", objectId, tenantId);
        }

        public string Username { get; }
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; }
    }

    [Fact]
    public void BuildScope_AppendsDefaultScope()
    {
        Assert.Equal(
            "https://contoso.operations.dynamics.com/.default",
            MsalInteractiveTokenProvider.BuildScope("https://contoso.operations.dynamics.com"));
    }

    [Fact]
    public void BuildScope_TrimsTrailingSlashBeforeAppending()
    {
        Assert.Equal(
            "https://contoso.crm.dynamics.com/.default",
            MsalInteractiveTokenProvider.BuildScope("https://contoso.crm.dynamics.com/"));
    }

    [Fact]
    public void BuildAuthority_CombinesBaseAndTenant()
    {
        Assert.Equal(
            "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111",
            MsalInteractiveTokenProvider.BuildAuthority("https://login.microsoftonline.com/", "11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public async Task SignOutAsync_EvictsThePersistedCacheEntryForTheClientAndTenant()
    {
        var store = new InMemoryMsalTokenCacheStore();
        // The provider keys its persisted blob by "{clientId}|{tenantId}".
        store.Save("11111111-1111-1111-1111-111111111111|22222222-2222-2222-2222-222222222222",
            new byte[] { 1, 2, 3 });
        var provider = new MsalInteractiveTokenProvider(store);

        await provider.SignOutAsync(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");

        Assert.Null(store.Load("11111111-1111-1111-1111-111111111111|22222222-2222-2222-2222-222222222222"));
    }

    [Theory]
    [InlineData("", "tenant", "https://x.operations.dynamics.com", "Client ID")]
    [InlineData("client", "", "https://x.operations.dynamics.com", "Tenant")]
    [InlineData("client", "tenant", "", "URL")]
    public async Task AcquireTokenAsync_MissingRequiredInput_ThrowsBeforeBrowser(string clientId, string tenantId, string resourceBaseUrl, string expectedMessageFragment)
    {
        var provider = new MsalInteractiveTokenProvider();
        var request = new InteractiveTokenRequest(clientId, tenantId, resourceBaseUrl);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => provider.AcquireTokenAsync(request));
        Assert.Contains(expectedMessageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // #168 low: silent renewal used to bind GetAccountsAsync().FirstOrDefault()
    // -----------------------------------------------------------------------

    [Fact]
    public void SelectAccount_PrefersTheLastSuccessfullyUsedAccount()
    {
        var stale = new FakeAccount("stale", TenantA);
        var wanted = new FakeAccount("wanted", TenantA);

        // Enumeration order puts the stale account first — FirstOrDefault would bind it, fail silent
        // renewal, and re-prompt. The remembered identifier must win.
        var chosen = MsalInteractiveTokenProvider.SelectAccount(
            new IAccount[] { stale, wanted }, wanted.HomeAccountId.Identifier, TenantA);

        Assert.Same(wanted, chosen);
    }

    [Fact]
    public void SelectAccount_FallsBackToAnAccountInTheAuthoritysTenant()
    {
        var otherTenant = new FakeAccount("other", TenantB);
        var sameTenant = new FakeAccount("same", TenantA);

        // Nothing remembered yet (first acquisition after a restart): a two-account cache must still not
        // bind the account from the wrong tenant just because it enumerates first.
        var chosen = MsalInteractiveTokenProvider.SelectAccount(
            new IAccount[] { otherTenant, sameTenant }, lastUsedAccountId: null, tenantId: TenantA);

        Assert.Same(sameTenant, chosen);
    }

    [Fact]
    public void SelectAccount_IgnoresARememberedAccountThatIsNoLongerCached()
    {
        var sameTenant = new FakeAccount("same", TenantA);

        // The remembered account was signed out elsewhere: fall through the priority list rather than
        // returning null and forcing a needless browser prompt.
        var chosen = MsalInteractiveTokenProvider.SelectAccount(
            new IAccount[] { sameTenant }, "gone.99999999-9999-9999-9999-999999999999", TenantA);

        Assert.Same(sameTenant, chosen);
    }

    [Fact]
    public void SelectAccount_FallsBackToTheFirstAccountWhenNothingMatches()
    {
        var first = new FakeAccount("first", TenantB);
        var second = new FakeAccount("second", TenantB);

        var chosen = MsalInteractiveTokenProvider.SelectAccount(
            new IAccount[] { first, second }, lastUsedAccountId: null, tenantId: TenantA);

        Assert.Same(first, chosen);
    }

    [Fact]
    public void SelectAccount_ReturnsNullForAnEmptyOrMissingCache()
    {
        // Null means "no cached account" → the caller goes interactive without pinning, which is the
        // correct experience for a first sign-in.
        Assert.Null(MsalInteractiveTokenProvider.SelectAccount(Array.Empty<IAccount>(), null, TenantA));
        Assert.Null(MsalInteractiveTokenProvider.SelectAccount(null, "anything", TenantA));
    }

    [Fact]
    public void SelectAccount_ToleratesAnUnknownTenantForm()
    {
        // The authority tenant can be a domain form, in which case no account's HomeAccountId.TenantId
        // (always a GUID) matches — selection must still return a usable account.
        var only = new FakeAccount("only", TenantA);

        Assert.Same(only, MsalInteractiveTokenProvider.SelectAccount(
            new IAccount[] { only }, lastUsedAccountId: null, tenantId: "contoso.onmicrosoft.com"));
    }
}
