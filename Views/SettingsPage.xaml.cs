using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tray2048.Services;
using Windows.ApplicationModel;

namespace Tray2048.Views;

/// <summary>Settings shown inside the flyout; there are too few to justify a window.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private readonly FlyoutPage _game;
    private bool _loading = true;

    public SettingsPage(Action goBack, FlyoutPage game)
    {
        InitializeComponent();
        _goBack = goBack;
        _game = game;
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
        _loading = false;
    }

    /// <summary>Re-reads state that may have changed elsewhere (Windows Settings, the game).</summary>
    public void OnShown()
    {
        _ = LoadStartupStateAsync();
        ShowBestScore();
        BackButton.Focus(FocusState.Programmatic);
    }

    private void ShowBestScore()
    {
        int best = _game.BestScore;
        BestDescription.Text = best > 0 ? $"Your best is {best:N0}." : "No best score yet.";
        ResetBestButton.IsEnabled = best > 0;
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync()
    {
        ShowStartupState(await StartupService.GetStateAsync());
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the game a click away after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void ConfirmResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetConfirmFlyout.Hide();
        _game.ResetBestScore();
        ShowBestScore();
        BackButton.Focus(FocusState.Programmatic);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();
}
