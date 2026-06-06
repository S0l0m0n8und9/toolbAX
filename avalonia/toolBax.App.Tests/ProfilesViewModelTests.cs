using System.Linq;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
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
}
