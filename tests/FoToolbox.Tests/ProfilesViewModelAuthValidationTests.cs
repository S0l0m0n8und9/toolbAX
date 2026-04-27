using FoToolbox.Core.Models;
using FoToolbox.Host.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class ProfilesViewModelAuthValidationTests
{
    [Trait("Category", "Auth")]
    [Fact]
    public async Task Save_Rejects_Invalid_Fo_TenantId()
    {
        var db = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ProfilesViewModel(db, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.Environment.Name = "Env";
            selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
            selected.Environment.TenantId = "not-a-tenant";

            await SaveAsync(vm);

            Assert.Contains("Tenant ID must be a GUID or verified domain", vm.Status, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(db);
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task Save_Rejects_Invalid_Ce_TenantId()
    {
        var db = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ProfilesViewModel(db, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.Environment.Name = "Env";
            selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
            selected.Environment.TenantId = "contoso.onmicrosoft.com";
            selected.DataverseEnvironment.BaseUrl = "https://org.crm.dynamics.com";
            selected.DataverseEnvironment.TenantId = "bad tenant";

            await SaveAsync(vm);

            Assert.Contains("CE tenant ID must be a GUID or verified domain", vm.Status, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(db);
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task Save_Requires_Ce_TenantId_When_Dataverse_BaseUrl_Is_Configured()
    {
        var db = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ProfilesViewModel(db, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.Environment.Name = "Env";
            selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
            selected.Environment.TenantId = "contoso.onmicrosoft.com";
            selected.DataverseEnvironment.BaseUrl = "https://org.crm.dynamics.com";
            selected.DataverseEnvironment.TenantId = "   ";

            await SaveAsync(vm);

            Assert.Contains("CE tenant ID is required", vm.Status, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(db);
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task BeginInteractiveReauth_For_ClientSecret_Profile_Shows_Status_Guidance()
    {
        var db = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ProfilesViewModel(db, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.FoPrincipal.AuthMode = AuthMode.ClientSecret;

            await vm.BeginInteractiveReauthAsync("Finance and Operations");

            Assert.Contains("updated client secret", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Set active", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(db);
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task BeginInteractiveReauth_For_BearerToken_Profile_Starts_Token_Acquisition()
    {
        var db = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ProfilesViewModel(db, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.Environment.Name = "Env";
            selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
            selected.Environment.TenantId = "contoso.onmicrosoft.com";
            selected.FoPrincipal.AuthMode = AuthMode.BearerToken;

            await vm.BeginInteractiveReauthAsync("Finance and Operations");

            Assert.Contains("FO bearer token", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(db);
        }
    }

    private static async Task SaveAsync(ProfilesViewModel vm)
    {
        var saveMethod = typeof(ProfilesViewModel).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(saveMethod);

        var task = Assert.IsAssignableFrom<Task<bool>>(saveMethod!.Invoke(vm, new object[] { false }));
        await task;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
