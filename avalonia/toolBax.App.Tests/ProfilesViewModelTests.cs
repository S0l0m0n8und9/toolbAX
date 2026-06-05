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
}
