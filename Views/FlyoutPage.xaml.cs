using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tray2048.Game;
using Tray2048.Services;
using Windows.System;

namespace Tray2048.Views;

/// <summary>
/// The game. The engine is saved after every move, so closing the flyout (or the app) never loses
/// a run. Nothing runs while the flyout is hidden: animations are storyboards that just stop.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private readonly GameEngine _engine = new();

    // The best score when this game began; beating it lights up the Best pill.
    private int _bestAtStart;

    public FlyoutPage()
    {
        InitializeComponent();
        _engine.Load(SettingsService.SavedGame);
        _bestAtStart = _engine.Score >= _engine.BestScore ? 0 : _engine.BestScore;

        Board.Swiped += (_, direction) => Play(direction);
        Board.OverlayChosen += Board_OverlayChosen;
        Board.Render(_engine, animate: false);
        UpdateStatus();

        if (_engine.IsGameOver)
        {
            Board.ShowGameOver(_engine.Score, _engine.CanUndo);
        }
    }

    /// <summary>The flyout opened: put the keyboard on the board so arrows work right away.</summary>
    public void OnShown()
    {
        Board.FocusOverlayOrBoard();

        // A new window hands initial focus to its first button once activation finishes; take it back.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Board.FocusOverlayOrBoard);
    }

    public void OnHidden() => Save();

    public void Dispose() => Save();

    public int BestScore => _engine.BestScore;

    public void ResetBestScore()
    {
        _engine.ResetBestScore();
        _bestAtStart = _engine.BestScore;
        UpdateStatus();
        Save();
    }

    private void Play(MoveDirection direction)
    {
        if (Board.IsOverlayOpen)
        {
            return;
        }

        bool wasNewBest = IsNewBest;
        MoveResult result = _engine.Move(direction);
        if (!result.Moved)
        {
            Board.Bump(direction);
            return;
        }

        Board.Apply(result);
        if (result.ScoreGained > 0)
        {
            ScoreGainText.Text = $"+{result.ScoreGained:N0}";
            ScoreGainStoryboard.Stop();
            ScoreGainStoryboard.Begin();
        }

        UpdateStatus();
        if (IsNewBest && !wasNewBest)
        {
            NewBestStoryboard.Begin();
        }

        Save();

        if (result.ReachedGoal)
        {
            Board.ShowWin();
        }
        else if (_engine.IsGameOver)
        {
            Board.ShowGameOver(_engine.Score, _engine.CanUndo);
        }
    }

    private bool IsNewBest => _bestAtStart > 0 && _engine.Score > _bestAtStart;

    public void StartNewGame()
    {
        _engine.NewGame();
        _bestAtStart = _engine.BestScore;
        Board.HideOverlay();
        Board.Render(_engine, animate: true);
        Board.Focus(FocusState.Programmatic);
        UpdateStatus();
        Save();
    }

    private void Undo()
    {
        if (!_engine.Undo())
        {
            return;
        }

        Board.HideOverlay(animate: false);
        Board.Render(_engine, animate: false);
        Board.Focus(FocusState.Programmatic);
        UpdateStatus();
        Save();
    }

    private void UpdateStatus()
    {
        ScoreText.Text = _engine.Score.ToString("N0");
        BestText.Text = _engine.BestScore.ToString("N0");
        UndoButton.IsEnabled = _engine.CanUndo;

        bool newBest = IsNewBest;
        BestLabel.Text = newBest ? "New best" : "Best";
        BestIcon.Glyph = newBest ? "" : "";
        if (newBest)
        {
            BestIcon.Foreground = (Brush)Application.Current.Resources["WinTitleBrush"];
        }
        else
        {
            BestIcon.ClearValue(IconElement.ForegroundProperty);
        }

        App.Current.SetTrayStatus($"Score {_engine.Score:N0} · Best {_engine.BestScore:N0}");
    }

    private void Save() => SettingsService.SavedGame = _engine.ToState();

    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
#if DEBUG
        if (IsModifierDown(VirtualKey.Control) && IsModifierDown(VirtualKey.Shift) && TryLoadDebugBoard(e.Key))
        {
            e.Handled = true;
            return;
        }
#endif

        // Leave arrows to the overlay's buttons, and Ctrl/Alt chords to accelerators.
        if (Board.IsOverlayOpen || IsModifierDown(VirtualKey.Control) || IsModifierDown(VirtualKey.Menu))
        {
            return;
        }

        MoveDirection? direction = e.Key switch
        {
            VirtualKey.Left or VirtualKey.A or VirtualKey.GamepadDPadLeft or VirtualKey.GamepadLeftThumbstickLeft => MoveDirection.Left,
            VirtualKey.Right or VirtualKey.D or VirtualKey.GamepadDPadRight or VirtualKey.GamepadLeftThumbstickRight => MoveDirection.Right,
            VirtualKey.Up or VirtualKey.W or VirtualKey.GamepadDPadUp or VirtualKey.GamepadLeftThumbstickUp => MoveDirection.Up,
            VirtualKey.Down or VirtualKey.S or VirtualKey.GamepadDPadDown or VirtualKey.GamepadLeftThumbstickDown => MoveDirection.Down,
            _ => null,
        };

        if (direction is { } d)
        {
            e.Handled = true;
            Play(d);
        }
    }

#if DEBUG
    // Ctrl+Shift+W / Ctrl+Shift+L: one move from winning / losing, to check the overlays by hand.
    private bool TryLoadDebugBoard(VirtualKey key)
    {
        int[]? cells = key switch
        {
            VirtualKey.W => [1024, 1024, 0, 0, 2, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            VirtualKey.L => [2, 4, 2, 4, 4, 2, 4, 2, 2, 4, 2, 4, 4, 2, 8, 8],
            _ => null,
        };
        if (cells is null)
        {
            return false;
        }

        _engine.Load(new GameState(cells, 20_000, _engine.BestScore, false));
        Board.HideOverlay(animate: false);
        Board.Render(_engine, animate: true);
        UpdateStatus();
        return true;
    }
#endif

    private static bool IsModifierDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void Board_OverlayChosen(object? sender, OverlayChoice choice)
    {
        switch (choice)
        {
            case OverlayChoice.KeepGoing:
                Board.HideOverlay();
                Board.Focus(FocusState.Programmatic);
                break;
            case OverlayChoice.NewGame:
                StartNewGame();
                break;
            case OverlayChoice.Undo:
                Undo();
                break;
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

    private void NewGameButton_Click(object sender, RoutedEventArgs e) => StartNewGame();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}
