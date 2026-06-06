using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class ProfilesViewModelTests
{
    [Fact]
    public void Loads_profiles_and_preselects_the_active_one()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());

        Assert.Equal(4, vm.Profiles.Count);
        Assert.Equal("dev-usmf", vm.Selected!.Id);   // FakeProfileStore default ActiveId
        Assert.True(vm.IsSelectedActive);
    }

    [Fact]
    public void Filter_matches_name_or_legal()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());

        vm.Search = "apac";
        Assert.Single(vm.Filtered);                   // matches name "APAC Prod"

        vm.Search = "DEMF";
        Assert.Single(vm.Filtered);                   // matches legal entity

        vm.Search = "";
        Assert.Equal(4, vm.Filtered.Count());
    }

    [Fact]
    public void SetActive_updates_active_id_and_persists_to_the_store()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        Assert.False(vm.IsSelectedActive);
        vm.SetActiveCommand.Execute(null);

        Assert.Equal("uat-eur", vm.ActiveId);
        Assert.Equal("uat-eur", store.ActiveId);
        Assert.True(vm.IsSelectedActive);
    }

    [Fact]
    public void Save_reports_status()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.SaveCommand.Execute(null);

        Assert.Contains("Saved", vm.Status);
    }

    [Fact]
    public void Selecting_a_profile_populates_the_editable_drafts()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        var apac = vm.Profiles.Single(p => p.Id == "prd-apac");

        vm.Selected = apac;

        Assert.Equal(apac.Name, vm.DraftName);
        Assert.Equal(apac.Url, vm.DraftUrl);
        Assert.Equal(apac.Tenant, vm.DraftTenant);
        Assert.Equal(apac.Legal, vm.DraftLegal);
        Assert.Equal(apac.Tier, vm.DraftTier);
    }

    [Fact]
    public void Save_persists_edited_fields_and_updates_the_list()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftName = "EMEA UAT (renamed)";
        vm.DraftUrl = "contoso-uat2.operations.dynamics.com";
        vm.SaveCommand.Execute(null);

        var saved = store.GetAll().Single(p => p.Id == "uat-eur");
        Assert.Equal("EMEA UAT (renamed)", saved.Name);
        Assert.Equal("contoso-uat2.operations.dynamics.com", saved.Url);

        // The list + selection reflect the edit.
        Assert.Equal("EMEA UAT (renamed)", vm.Selected!.Name);
        Assert.Contains(vm.Profiles, p => p.Name == "EMEA UAT (renamed)");
    }

    [Fact]
    public void Save_raises_profile_saved_with_the_updated_profile()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        EnvProfile? saved = null;
        vm.ProfileSaved += p => saved = p;

        vm.DraftName = "Renamed";
        vm.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal("uat-eur", saved!.Id);
        Assert.Equal("Renamed", saved.Name);
    }

    [Fact]
    public void Reselecting_discards_uncommitted_edits()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        var uat = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.Selected = uat;

        vm.DraftName = "scratch edit";
        vm.Selected = vm.Profiles.Single(p => p.Id == "prd-apac");
        vm.Selected = uat;

        Assert.Equal(uat.Name, vm.DraftName); // edit was not committed
    }

    [Fact]
    public void Add_profile_creates_selects_and_persists_a_new_environment()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        var before = vm.Profiles.Count;
        EnvProfile? added = null;
        vm.ProfileSaved += p => added = p;

        vm.AddProfileCommand.Execute(null);

        Assert.Equal(before + 1, vm.Profiles.Count);
        Assert.NotNull(vm.Selected);
        Assert.Equal(vm.Selected, added);
        Assert.Contains(store.GetAll(), p => p.Id == vm.Selected!.Id); // persisted
    }

    [Fact]
    public void Delete_profile_removes_it_and_reports_the_id()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        string? deletedId = null;
        vm.ProfileDeleted += id => deletedId = id;

        vm.DeleteProfileCommand.Execute(null);

        Assert.Equal("uat-eur", deletedId);
        Assert.DoesNotContain(vm.Profiles, p => p.Id == "uat-eur");
        Assert.DoesNotContain(store.GetAll(), p => p.Id == "uat-eur");
        Assert.NotNull(vm.Selected); // reselected another
    }

    [Fact]
    public void Deleting_the_active_profile_clears_the_active_id()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.SetActiveCommand.Execute(null);
        Assert.Equal("uat-eur", vm.ActiveId);

        vm.DeleteProfileCommand.Execute(null);

        Assert.Null(vm.ActiveId); // no longer points at the deleted profile
    }

    [Fact]
    public void Deleting_reselects_the_adjacent_profile()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        var ordered = vm.Profiles.ToList();
        vm.Selected = ordered[1];

        vm.DeleteProfileCommand.Execute(null);

        Assert.Equal(ordered[2].Id, vm.Selected!.Id); // the item that followed, not the top
    }

    [Fact]
    public void Storing_a_secret_marks_it_present_and_clears_the_input()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        var uat = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.Selected = uat;

        vm.SecretInput = "super-secret-value";
        vm.SaveSecretCommand.Execute(null);

        Assert.True(vm.HasSecret);
        Assert.True(secrets.HasSecret("uat-eur"));
        Assert.Equal(string.Empty, vm.SecretInput); // plaintext not retained in the VM
    }

    [Fact]
    public void Clearing_a_secret_removes_it()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.SecretInput = "x";
        vm.SaveSecretCommand.Execute(null);

        vm.ClearSecretCommand.Execute(null);

        Assert.False(vm.HasSecret);
        Assert.False(secrets.HasSecret("uat-eur"));
    }

    [Fact]
    public void Empty_secret_input_is_not_stored()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), new FakeSecretStore());
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.SecretInput = "";
        vm.SaveSecretCommand.Execute(null);

        Assert.False(vm.HasSecret);
    }

    [Fact]
    public async Task Test_connection_reports_success_when_a_token_is_acquired()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), auth: new FakeAuthService());
        vm.Selected = vm.Profiles.First();

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Contains("Connected", vm.Status);
        Assert.False(vm.IsTestingConnection);
    }

    [Fact]
    public async Task Test_connection_reports_the_failure_message()
    {
        var failing = new FakeAuthService(_ => throw new InvalidOperationException("AADSTS700016: app not found"));
        var vm = new ProfilesViewModel(new FakeProfileStore(), auth: failing);
        vm.Selected = vm.Profiles.First();

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Contains("failed", vm.Status);
        Assert.Contains("AADSTS700016", vm.Status);
        Assert.False(vm.IsTestingConnection);
    }

    [Fact]
    public void Fo_service_principal_drafts_persist_and_reload()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftClientId = "client-xyz";
        vm.DraftAuthMode = FoAuthMode.Certificate;
        vm.SaveCommand.Execute(null);

        Assert.Equal("client-xyz", store.GetAll().Single(p => p.Id == "uat-eur").ClientId);

        // Reselect away and back: drafts reload from the saved profile.
        vm.Selected = vm.Profiles.Single(p => p.Id == "dev-usmf");
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        Assert.Equal("client-xyz", vm.DraftClientId);
        Assert.Equal(FoAuthMode.Certificate, vm.DraftAuthMode);
    }

    [Fact]
    public void Dataverse_service_principal_drafts_persist_and_reload()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftDataverseClientId = "dv-client-xyz";
        vm.DraftDataverseAuthMode = FoAuthMode.Certificate;
        vm.SaveCommand.Execute(null);

        var saved = store.GetAll().Single(p => p.Id == "uat-eur");
        Assert.Equal("dv-client-xyz", saved.DataverseClientId);
        Assert.Equal(FoAuthMode.Certificate, saved.DataverseAuthMode);

        // Reselect away and back: drafts reload from the saved profile.
        vm.Selected = vm.Profiles.Single(p => p.Id == "dev-usmf");
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        Assert.Equal("dv-client-xyz", vm.DraftDataverseClientId);
        Assert.Equal(FoAuthMode.Certificate, vm.DraftDataverseAuthMode);
    }

    [Fact]
    public void Dataverse_secret_is_stored_under_the_dataverse_target()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        var selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.Selected = selected;

        vm.DataverseSecretInput = "dv-secret";
        vm.SaveDataverseSecretCommand.Execute(null);

        Assert.True(vm.HasDataverseSecret);
        Assert.True(secrets.HasSecret(selected.Id, SecretTarget.Dataverse));
        Assert.False(secrets.HasSecret(selected.Id)); // the F&O (default) secret is untouched
        Assert.Equal(string.Empty, vm.DataverseSecretInput); // plaintext not retained

        vm.ClearDataverseSecretCommand.Execute(null);
        Assert.False(vm.HasDataverseSecret);
    }

    // A store that can't persist (e.g. no service principal yet) must not report a false success or
    // silently discard the secret the user typed.
    private sealed class NoOpSecretStore : ToolBax.Core.Services.ISecretStore
    {
        public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) => false;
        public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) { }
        public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) { }
    }

    [Fact]
    public void Storing_a_dataverse_secret_that_cannot_persist_keeps_the_entry_and_reports_no_success()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), new NoOpSecretStore());
        vm.Selected = vm.Profiles.First();
        vm.DataverseSecretInput = "dv-secret";

        vm.SaveDataverseSecretCommand.Execute(null);

        Assert.False(vm.HasDataverseSecret);
        Assert.Equal("dv-secret", vm.DataverseSecretInput);     // not discarded
        Assert.DoesNotContain("stored", vm.Status);             // no false confirmation
    }

    [Fact]
    public void Dataverse_web_api_is_derived_from_the_edited_url()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();

        vm.DraftDataverseUrl = "contoso.crm.dynamics.com";
        Assert.Equal("contoso.crm.dynamics.com/api/data/v9.2", vm.DataverseWebApi);

        vm.DraftDataverseUrl = "";
        Assert.Equal(string.Empty, vm.DataverseWebApi);
    }

    [Fact]
    public void Save_persists_the_dataverse_url()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftDataverseUrl = "contoso-uat.crm.dynamics.com";
        vm.SaveCommand.Execute(null);

        Assert.Equal("contoso-uat.crm.dynamics.com", store.GetAll().Single(p => p.Id == "uat-eur").DataverseUrl);
    }

    [Fact]
    public void Has_secret_tracks_the_selection()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        var uat = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.Selected = uat;
        vm.SecretInput = "x";
        vm.SaveSecretCommand.Execute(null);

        vm.Selected = vm.Profiles.Single(p => p.Id == "prd-apac");
        Assert.False(vm.HasSecret);

        vm.Selected = uat;
        Assert.True(vm.HasSecret);
    }

    [Fact]
    public void Di_mode_toggles_ropc_and_interactive_visibility()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();

        vm.DraftDiMode = DiAuthMode.Ropc;
        Assert.True(vm.IsRopc);
        Assert.False(vm.IsInteractive);

        vm.DraftDiMode = DiAuthMode.Interactive;
        Assert.False(vm.IsRopc);
        Assert.True(vm.IsInteractive);
    }

    [Fact]
    public void Save_persists_di_client_id_and_mode()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftDiClientId = "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b";
        vm.DraftDiMode = DiAuthMode.Ropc;
        vm.SaveCommand.Execute(null);

        var saved = store.GetAll().Single(p => p.Id == "uat-eur");
        Assert.Equal("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", saved.DataIntegratorClientId);
        Assert.Equal(DiAuthMode.Ropc, saved.DataIntegratorMode);
    }

    [Fact]
    public void Di_service_account_secret_is_stored_under_a_separate_key()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DiSecretInput = "svc-password";
        vm.SaveDiSecretCommand.Execute(null);

        Assert.True(vm.HasDiSecret);
        Assert.True(secrets.HasSecret("uat-eur:di"));
        Assert.False(secrets.HasSecret("uat-eur")); // distinct from the Auth client secret
        Assert.Equal(string.Empty, vm.DiSecretInput);
    }

    [Fact]
    public async Task Interactive_sign_in_reports_the_account()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftDiMode = DiAuthMode.Interactive;
        vm.DraftDiClientId = "client-id";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("Signed in as", vm.DiStatus);
        Assert.False(vm.IsSigningIn);
    }

    [Fact]
    public async Task Sign_in_without_a_client_id_prompts_for_one()
    {
        var broker = new ThrowingBroker();
        var vm = new ProfilesViewModel(new FakeProfileStore(), broker: broker);
        vm.Selected = vm.Profiles.First();
        vm.DraftDiMode = DiAuthMode.Interactive;
        vm.DraftDiClientId = "";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("client ID", vm.DiStatus);
        Assert.False(broker.WasCalled); // guarded before reaching the broker
    }

    [Fact]
    public async Task Cancelled_sign_in_reports_cancellation_not_failure()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), broker: new CancellingBroker());
        vm.Selected = vm.Profiles.First();
        vm.DraftDiMode = DiAuthMode.Interactive;
        vm.DraftDiClientId = "client-id";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Sign-in cancelled.", vm.DiStatus);
    }

    [Fact]
    public void Changing_di_mode_clears_the_status()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftDiMode = DiAuthMode.Ropc;
        vm.DiSecretInput = "x";
        vm.SaveDiSecretCommand.Execute(null);
        Assert.NotEqual(string.Empty, vm.DiStatus);

        vm.DraftDiMode = DiAuthMode.Interactive;

        Assert.Equal(string.Empty, vm.DiStatus);
    }

    private sealed class ThrowingBroker : IInteractiveAuthBroker
    {
        public bool WasCalled { get; private set; }

        public Task<AuthResult?> SignInAsync(string clientId, string tenant, CancellationToken ct = default)
        {
            WasCalled = true;
            throw new System.InvalidOperationException("broker should not be called");
        }
    }

    private sealed class CancellingBroker : IInteractiveAuthBroker
    {
        public Task<AuthResult?> SignInAsync(string clientId, string tenant, CancellationToken ct = default) =>
            throw new System.OperationCanceledException();
    }
}
