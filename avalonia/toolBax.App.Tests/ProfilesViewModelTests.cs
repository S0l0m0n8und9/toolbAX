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
}
