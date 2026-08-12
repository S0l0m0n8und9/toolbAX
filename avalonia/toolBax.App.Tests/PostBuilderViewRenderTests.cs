using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for the POST Builder (control-map §7).</summary>
public class PostBuilderViewRenderTests
{
    [AvaloniaFact]
    public void Renders_method_combo_path_and_send_button()
    {
        var view = new PostBuilderView { DataContext = new PostBuilderViewModel(new FakeODataClient()) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(view.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault());
            var send = view.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Send");
            Assert.True(send.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_include_column_header_explains_what_a_blank_included_field_means_on_patch()
    {
        // On a PATCH the checkbox carries the omit/clear distinction (#158), which isn't guessable from a
        // column called "Incl" — so the tip has to actually reach the rendered header, not just the markup.
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new FakeMetadataService())
        {
            Method = "PATCH",
            UseFieldGrid = true,
        };
        var view = new PostBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var header = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == "Incl");

            Assert.Contains("clears the field", (string)ToolTip.GetTip(header)!);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Load_error_banner_renders_in_both_raw_and_grid_mode()
    {
        var vm = new PostBuilderViewModel(new FakeODataClient());
        var view = new PostBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "catalogue endpoint unreachable");

            // Raw mode: no picker, no issue panel — the banner is the only signal.
            vm.LoadError = "catalogue endpoint unreachable"; // e.g. an Initialize failure
            Dispatcher.UIThread.RunJobs();

            var banner = view.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == "catalogue endpoint unreachable");
            Assert.NotNull(banner);
            Assert.True(banner!.IsVisible); // raw mode (UseFieldGrid is false here) — not hidden

            // Entering grid mode selects an entity, whose own (fresh) load re-derives LoadError — see the
            // per-entity ownership tests in PostBuilderViewModelTests for that. What THIS test proves is
            // the binding itself: the banner isn't gated on UseFieldGrid either way, so re-asserting the
            // same error after the switch must render identically to the raw-mode case above.
            vm.UseFieldGrid = true;
            vm.LoadError = "catalogue endpoint unreachable";
            Dispatcher.UIThread.RunJobs();

            banner = view.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == "catalogue endpoint unreachable");
            Assert.NotNull(banner);
            Assert.True(banner!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    // Holds a send open so the busy-only Cancel button can be observed while it is in flight.
    private sealed class GatedODataClient : IODataClient
    {
        public readonly TaskCompletionSource Gate = new();

        public async Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            await Gate.Task;
            ct.ThrowIfCancellationRequested();
            return new ODataResponse(204, "No Content", string.Empty, 5);
        }
    }

    [AvaloniaFact]
    public void Cancel_appears_only_while_sending_and_stops_it()
    {
        var client = new GatedODataClient();
        var vm = new PostBuilderViewModel(client) { Method = "POST" };
        var view = new PostBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // SendCancelCommand was generated (IncludeCancelCommand) but bound nowhere, so no cancellation
            // was reachable from the view (#168).
            var cancel = view.GetVisualDescendants().OfType<Button>()
                .Single(b => (b.Content as string) == "Cancel");
            Assert.False(cancel.IsVisible); // idle: nothing to cancel

            var send = vm.SendCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(cancel.IsVisible);
            Assert.True(cancel.Command!.CanExecute(null));

            cancel.Command.Execute(null);
            client.Gate.SetResult();
            Dispatcher.UIThread.RunJobs();
            send.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Send cancelled.", vm.StatusText);
            Assert.False(cancel.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
