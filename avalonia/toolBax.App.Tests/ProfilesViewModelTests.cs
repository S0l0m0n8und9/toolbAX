using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
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
        // "Prod" tier normalises to the Production environment-type bucket.
        Assert.Equal(EnvProfile.ProductionType, vm.DraftEnvironmentType);
    }

    [Fact]
    public void Environment_type_normalizes_from_the_stored_tier()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());

        // "Prod" → Production; "Tier 1"/"Tier 2"/"Sandbox" → Non-production.
        vm.Selected = vm.Profiles.Single(p => p.Id == "prd-apac");
        Assert.Equal(EnvProfile.ProductionType, vm.DraftEnvironmentType);

        vm.Selected = vm.Profiles.Single(p => p.Id == "dev-usmf");
        Assert.Equal(EnvProfile.NonProductionType, vm.DraftEnvironmentType);

        vm.Selected = vm.Profiles.Single(p => p.Id == "sbx-fin");
        Assert.Equal(EnvProfile.NonProductionType, vm.DraftEnvironmentType);
    }

    [Fact]
    public void Save_writes_the_environment_type_into_tier_and_subtitle()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "dev-usmf");

        vm.DraftEnvironmentType = EnvProfile.ProductionType;
        vm.SaveCommand.Execute(null);

        var saved = store.GetAll().Single(p => p.Id == "dev-usmf");
        Assert.Equal(EnvProfile.ProductionType, saved.Tier);
        Assert.Equal(EnvProfile.ProductionType, saved.EnvironmentType);
        Assert.EndsWith(EnvProfile.ProductionType, saved.Subtitle); // "USMF · Production"
    }

    [Fact]
    public void Delete_is_disabled_only_when_one_profile_remains()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        Assert.True(vm.CanDeleteProfile); // seeded with 4

        // Delete down to the last one.
        while (vm.Profiles.Count > 1)
        {
            vm.Selected = vm.Profiles[0];
            vm.DeleteProfileCommand.Execute(null);
        }

        Assert.Single(vm.Profiles);
        Assert.False(vm.CanDeleteProfile);
    }

    [Fact]
    public void Set_active_label_and_enablement_track_the_active_selection()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        var inactive = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.Selected = inactive;
        Assert.Equal("Set active", vm.SetActiveLabel);
        Assert.True(vm.CanSetActive);

        vm.SetActiveCommand.Execute(null);
        Assert.Equal("Active", vm.SetActiveLabel);
        Assert.False(vm.CanSetActive);
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
    public async Task Test_connection_reports_success_when_the_metadata_probe_passes()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), connectionTester: new FakeConnectionTester());
        vm.Selected = vm.Profiles.First();

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Contains("Connected", vm.Status);
        Assert.False(vm.IsTestingFoConnection);
    }

    [Fact]
    public async Task Test_connection_reports_the_probe_failure_message()
    {
        // A token can be minted yet the metadata endpoint rejects it — the probe (not just token
        // acquisition) is what the status must reflect.
        var tester = new FakeConnectionTester(fo: _ => new ConnectionTestResult(false, "401 Unauthorized (AADSTS700016)"));
        var vm = new ProfilesViewModel(new FakeProfileStore(), connectionTester: tester);
        vm.Selected = vm.Profiles.First();

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Contains("failed", vm.Status);
        Assert.Contains("AADSTS700016", vm.Status);
        Assert.False(vm.IsTestingFoConnection);
    }

    [Fact]
    public async Task Test_dataverse_connection_reports_success_when_the_probe_passes()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), connectionTester: new FakeConnectionTester());
        vm.Selected = vm.Profiles.First();

        await vm.TestDataverseConnectionCommand.ExecuteAsync(null);

        Assert.Contains("Dataverse", vm.Status);
        Assert.Contains("Connected", vm.Status);
        Assert.False(vm.IsTestingDataverseConnection);
    }

    [Fact]
    public async Task Test_dataverse_connection_reports_the_probe_failure_message()
    {
        var tester = new FakeConnectionTester(dataverse: _ => new ConnectionTestResult(false, "AADSTS500011: resource not found"));
        var vm = new ProfilesViewModel(new FakeProfileStore(), connectionTester: tester);
        vm.Selected = vm.Profiles.First();

        await vm.TestDataverseConnectionCommand.ExecuteAsync(null);

        Assert.Contains("Dataverse", vm.Status);
        Assert.Contains("failed", vm.Status);
        Assert.Contains("AADSTS500011", vm.Status);
        Assert.False(vm.IsTestingDataverseConnection);
    }

    [Fact]
    public async Task Sign_out_evicts_the_selected_profiles_cached_session()
    {
        var auth = new FakeAuthService();
        var vm = new ProfilesViewModel(new FakeProfileStore(), auth: auth);
        vm.Selected = vm.Profiles.First();

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(vm.Selected!.Id, auth.LastSignedOut?.Id);
        Assert.Contains("Signed out", vm.Status);
    }

    [Fact]
    public void Saving_an_auth_config_change_evicts_the_old_cached_session()
    {
        var auth = new FakeAuthService();
        var vm = new ProfilesViewModel(new FakeProfileStore(), auth: auth);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftClientId = "11111111-changed-client-id";
        vm.SaveCommand.Execute(null);

        // Changing the client id makes any cached token for the old identity stale → evict it.
        Assert.Equal("uat-eur", auth.LastSignedOut?.Id);
    }

    [Fact]
    public void Saving_a_non_auth_change_does_not_evict_the_session()
    {
        var auth = new FakeAuthService();
        var vm = new ProfilesViewModel(new FakeProfileStore(), auth: auth);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        // First save normalises drafts↔store (a fresh interactive profile auto-fills its client id).
        vm.SaveCommand.Execute(null);
        var evictionsAfterNormalize = auth.SignOutCount;

        // A pure rename changes nothing about the auth identity, so it must not force a re-auth.
        vm.DraftName = "EMEA UAT (renamed)";
        vm.SaveCommand.Execute(null);

        Assert.Equal(evictionsAfterNormalize, auth.SignOutCount);
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
    public void Di_default_client_id_matches_the_canonical_dual_write_constant()
    {
        // The VM-facing default (ToolBax.Core.Models) duplicates the canonical FoToolbox value so the
        // models layer needn't reference FoToolbox.Core — this guards against the two drifting apart.
        Assert.Equal(DualWriteAuthConstants.ClientId, DiAuthModeExtensions.DefaultDataIntegratorClientId);
    }

    [Fact]
    public void Di_client_id_defaults_to_the_well_known_first_party_app_when_unset()
    {
        // The Data Integrator is a well-known first-party Microsoft app — the user shouldn't have to
        // supply a client id (the WPF/original tool never does). A profile with no DI client id surfaces
        // the well-known default so sign-in works out of the box.
        var vm = new ProfilesViewModel(new FakeProfileStore());

        vm.Selected = vm.Profiles.First();

        Assert.Equal(DiAuthModeExtensions.DefaultDataIntegratorClientId, vm.DraftDiClientId);
        Assert.True(vm.ShowDiDefaultClientIdNote);
    }

    [Fact]
    public void Changing_di_client_id_hides_the_default_note()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        Assert.True(vm.ShowDiDefaultClientIdNote);

        vm.DraftDiClientId = "11111111-2222-3333-4444-555555555555";

        Assert.False(vm.ShowDiDefaultClientIdNote);
    }

    [Fact]
    public void Existing_custom_di_client_id_is_preserved_over_the_default()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");
        vm.DraftDiClientId = "custom-di-app-id";
        vm.SaveCommand.Execute(null);

        // Re-select to reload from the store: a configured custom id is kept, not overwritten by the default.
        vm.Selected = vm.Profiles.First(p => p.Id != "uat-eur");
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        Assert.Equal("custom-di-app-id", vm.DraftDiClientId);
        Assert.False(vm.ShowDiDefaultClientIdNote);
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
    public void Di_service_account_secret_is_stored_under_its_own_target()
    {
        var secrets = new FakeSecretStore();
        var vm = new ProfilesViewModel(new FakeProfileStore(), secrets);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DiSecretInput = "svc-password";
        vm.SaveDiSecretCommand.Execute(null);

        Assert.True(vm.HasDiSecret);
        // The environment id is passed through unchanged; the target is what separates the DI
        // service-account secret from the environment's F&O client secret.
        Assert.True(secrets.HasSecret("uat-eur", SecretTarget.DataIntegrator));
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

    [Fact]
    public async Task Test_gateway_reports_the_tester_result()
    {
        var tester = new FakeDualWriteGatewayTester(new DwGatewayTestResult(true, "Linked: Contoso (cid abc)."));
        var vm = new ProfilesViewModel(new FakeProfileStore(), gatewayTester: tester);
        vm.Selected = vm.Profiles.First(); // seeded profiles have an F&O URL

        await vm.TestGatewayCommand.ExecuteAsync(null);

        Assert.Equal("Linked: Contoso (cid abc).", vm.DiStatus);
        Assert.False(vm.IsTestingGateway);
    }

    [Fact]
    public async Task Test_gateway_requires_an_fo_url()
    {
        // The gateway host is discovered during portal sign-in; the only prerequisite is the F&O URL
        // (the portal's axenv identifier).
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftUrl = string.Empty;

        await vm.TestGatewayCommand.ExecuteAsync(null);

        Assert.Contains("F&O environment URL", vm.DiStatus);
    }

    [Fact]
    public void Auth_modes_list_includes_interactive_and_new_profiles_default_to_it()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());

        Assert.Equal(new[] { FoAuthMode.Interactive, FoAuthMode.ClientSecret, FoAuthMode.Certificate }, vm.AuthModes);

        vm.AddProfileCommand.Execute(null); // a brand-new environment
        Assert.Equal(FoAuthMode.Interactive, vm.DraftAuthMode);
        Assert.Equal(FoAuthMode.Interactive, vm.DraftDataverseAuthMode);
    }

    [Fact]
    public void Selecting_interactive_fo_fills_the_default_client_id_and_shows_the_note()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftAuthMode = FoAuthMode.ClientSecret; // move off Interactive…
        vm.DraftClientId = string.Empty;            // …and clear the id

        vm.DraftAuthMode = FoAuthMode.Interactive;

        Assert.Equal(FoAuthModeExtensions.DefaultInteractiveClientId, vm.DraftClientId);
        Assert.True(vm.ShowFoDefaultClientIdNote);
        Assert.False(vm.IsFoClientSecretMode); // no client-secret entry for Interactive

        vm.DraftClientId = "my-own-client-id"; // editing away from the default hides the note
        Assert.False(vm.ShowFoDefaultClientIdNote);
    }

    [Fact]
    public void Interactive_does_not_overwrite_an_existing_client_id()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftAuthMode = FoAuthMode.ClientSecret;
        vm.DraftClientId = "already-set";

        vm.DraftAuthMode = FoAuthMode.Interactive; // a non-blank id is respected

        Assert.Equal("already-set", vm.DraftClientId);
        Assert.False(vm.ShowFoDefaultClientIdNote);
    }

    [Fact]
    public void Selecting_interactive_dataverse_fills_the_default_client_id_and_shows_the_note()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();
        vm.DraftDataverseAuthMode = FoAuthMode.ClientSecret;
        vm.DraftDataverseClientId = string.Empty;

        vm.DraftDataverseAuthMode = FoAuthMode.Interactive;

        Assert.Equal(FoAuthModeExtensions.DefaultInteractiveClientId, vm.DraftDataverseClientId);
        Assert.True(vm.ShowDataverseDefaultClientIdNote);
        Assert.False(vm.IsDataverseClientSecretMode);

        vm.DraftDataverseClientId = "custom-dv-id";
        Assert.False(vm.ShowDataverseDefaultClientIdNote);
    }

    [Fact]
    public void Client_secret_mode_flag_tracks_the_fo_auth_mode()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore());
        vm.Selected = vm.Profiles.First();

        vm.DraftAuthMode = FoAuthMode.ClientSecret;
        Assert.True(vm.IsFoClientSecretMode);
        vm.DraftAuthMode = FoAuthMode.Certificate;
        Assert.False(vm.IsFoClientSecretMode);
    }

    [Fact]
    public void Interactive_fo_auth_mode_persists_and_reloads()
    {
        var store = new FakeProfileStore();
        var vm = new ProfilesViewModel(store);
        vm.Selected = vm.Profiles.Single(p => p.Id == "uat-eur");

        vm.DraftAuthMode = FoAuthMode.Interactive;
        vm.DraftDataverseAuthMode = FoAuthMode.Interactive;
        vm.SaveCommand.Execute(null);

        var saved = store.GetAll().Single(p => p.Id == "uat-eur");
        Assert.Equal(FoAuthMode.Interactive, saved.AuthMode);
        Assert.Equal(FoAuthMode.Interactive, saved.DataverseAuthMode);
    }

    // A store whose SetSecret fails outright — what the real CoreSecretStore does when no DPAPI vault is
    // available. The command must surface that, not let it escape as an unhandled exception.
    private sealed class ThrowingSecretStore : ToolBax.Core.Services.ISecretStore
    {
        public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) => false;

        public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) =>
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");

        public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) { }
    }

    [Fact]
    public void Storing_a_di_secret_that_cannot_persist_keeps_the_entry_and_reports_no_success()
    {
        // The DI secret used to be written under a key the real store didn't recognise: nothing was
        // stored, yet the UI cleared the box and said "Service-account secret stored."
        var vm = new ProfilesViewModel(new FakeProfileStore(), new NoOpSecretStore());
        vm.Selected = vm.Profiles.First();
        vm.DiSecretInput = "svc-password";

        vm.SaveDiSecretCommand.Execute(null);

        Assert.False(vm.HasDiSecret);
        Assert.Equal("svc-password", vm.DiSecretInput);        // not discarded
        Assert.DoesNotContain("secret stored", vm.DiStatus);   // no false confirmation
    }

    [Fact]
    public void Di_secret_storage_failure_is_reported_not_thrown()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), new ThrowingSecretStore());
        vm.Selected = vm.Profiles.First();
        vm.DiSecretInput = "svc-password";

        vm.SaveDiSecretCommand.Execute(null);

        Assert.Contains("Could not store", vm.DiStatus);
        Assert.Contains("DPAPI", vm.DiStatus);                 // the underlying reason surfaces
        Assert.Equal("svc-password", vm.DiSecretInput);         // not discarded
        Assert.False(vm.HasDiSecret);
    }

    [Fact]
    public void Client_secret_storage_failure_is_reported_not_thrown()
    {
        var vm = new ProfilesViewModel(new FakeProfileStore(), new ThrowingSecretStore());
        vm.Selected = vm.Profiles.First();
        vm.SecretInput = "super-secret";

        vm.SaveSecretCommand.Execute(null);

        Assert.Contains("Could not store", vm.Status);
        Assert.Equal("super-secret", vm.SecretInput);           // not discarded
        Assert.False(vm.HasSecret);
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
