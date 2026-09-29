using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Tray2048.Game;
using Windows.Foundation;

namespace Tray2048.Views;

public enum OverlayChoice
{
    KeepGoing,
    NewGame,
    Undo,
}

/// <summary>
/// Draws the board and animates each move: tiles glide to their new cells, merged pairs pop into
/// one bigger tile, and the new tile grows in once the slide lands. Input arriving mid-animation
/// snaps the current move to its end first, so fast play never falls behind.
/// </summary>
public sealed partial class GameBoard : UserControl
{
    private const double CellStep = 78; // 70 px tile + 8 px gutter
    private const double SwipeThreshold = 24;

    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan AppearDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WinOverlayDelay = TimeSpan.FromMilliseconds(450);

    private readonly Dictionary<int, TileView> _tiles = [];
    private readonly DispatcherQueueTimer _overlayTimer;
    private Storyboard? _slide;
    private Action? _finishSlide;
    private Storyboard? _bump;
    private bool _overlayIsWin;
    private Point? _pressPoint;

    public GameBoard()
    {
        InitializeComponent();
        _overlayTimer = DispatcherQueue.CreateTimer();
        _overlayTimer.IsRepeating = false;
        _overlayTimer.Tick += (_, _) => ShowOverlayNow();
        OverlayHideStoryboard.Completed += (_, _) =>
        {
            if (!IsOverlayOpen)
            {
                Overlay.Visibility = Visibility.Collapsed;
            }
        };
    }

    public event EventHandler<MoveDirection>? Swiped;

    public event EventHandler<OverlayChoice>? OverlayChosen;

    public bool IsOverlayOpen { get; private set; }

    /// <summary>Redraws every tile from the engine; <paramref name="animate"/> grows them in.</summary>
    public void Render(GameEngine engine, bool animate)
    {
        FinishSlide();
        TileLayer.Children.Clear();
        _tiles.Clear();

        int order = 0;
        for (int cell = 0; cell < GameEngine.CellCount; cell++)
        {
            int value = engine.ValueAt(cell);
            if (value == 0)
            {
                continue;
            }

            TileView tile = AddTile(engine.IdAt(cell), value, cell);
            if (animate)
            {
                Appear(tile, TimeSpan.FromMilliseconds(order++ * 25));
            }
        }
    }

    public void Apply(MoveResult result)
    {
        FinishSlide();

        Storyboard slide = new();
        foreach (TileSlide move in result.Slides)
        {
            if (!_tiles.TryGetValue(move.TileId, out TileView? tile))
            {
                continue;
            }

            (double x, double y) = PositionOf(move.To);
            CompositeTransform t = tile.Position;

            // The final position is the local value, so stopping the storyboard early lands there.
            AddSlide(slide, t, "TranslateX", t.TranslateX, x);
            AddSlide(slide, t, "TranslateY", t.TranslateY, y);
            t.TranslateX = x;
            t.TranslateY = y;
        }

        _finishSlide = () =>
        {
            foreach (TileMerge merge in result.Merges)
            {
                RemoveTile(merge.FirstId);
                RemoveTile(merge.SecondId);
                Pop(AddTile(merge.NewId, merge.Value, merge.Cell));
            }

            if (result.Spawn is { } spawn)
            {
                Appear(AddTile(spawn.TileId, spawn.Value, spawn.Cell), TimeSpan.Zero);
            }
        };

        slide.Completed += (_, _) =>
        {
            if (_slide == slide)
            {
                FinishSlide();
            }
        };
        _slide = slide;
        slide.Begin();
    }

    /// <summary>A move that changes nothing nudges the tiles toward the wall instead.</summary>
    public void Bump(MoveDirection direction)
    {
        _bump?.Stop();

        (double dx, double dy) = direction switch
        {
            MoveDirection.Left => (-1, 0),
            MoveDirection.Right => (1, 0),
            MoveDirection.Up => (0, -1),
            _ => (0, 1),
        };

        Storyboard bump = new();
        bump.Children.Add(BumpAnimation(dx == 0 ? "Y" : "X", 5 * (dx + dy)));
        _bump = bump;
        bump.Begin();
    }

    public void ShowWin()
    {
        _overlayIsWin = true;
        OverlayTitle.Text = "You win";
        OverlayTitle.Foreground = (Brush)Application.Current.Resources["WinTitleBrush"];
        OverlayMessage.Text = "You made the 2048 tile. Keep going for a bigger one?";
        OverlayPrimaryButton.Content = "Keep going";
        OverlaySecondaryButton.Content = "New game";
        OverlaySecondaryButton.Visibility = Visibility.Visible;
        _overlayTimer.Interval = WinOverlayDelay;
        _overlayTimer.Start();
        IsOverlayOpen = true;
    }

    public void ShowGameOver(int score, bool canUndo)
    {
        _overlayIsWin = false;
        OverlayTitle.Text = "Game over";
        OverlayTitle.ClearValue(TextBlock.ForegroundProperty);
        OverlayMessage.Text = $"No moves left. You scored {score:N0}.";
        OverlayPrimaryButton.Content = "Try again";
        OverlaySecondaryButton.Content = "Undo";
        OverlaySecondaryButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        _overlayTimer.Interval = AppearDuration;
        _overlayTimer.Start();
        IsOverlayOpen = true;
    }

    public void HideOverlay(bool animate = true)
    {
        _overlayTimer.Stop();
        if (!IsOverlayOpen)
        {
            return;
        }

        IsOverlayOpen = false;
        OverlayShowStoryboard.Stop();
        if (animate && Overlay.Visibility == Visibility.Visible)
        {
            OverlayHideStoryboard.Begin();
        }
        else
        {
            OverlayHideStoryboard.Stop();
            Overlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Puts focus on the button a keyboard player most likely wants.</summary>
    public void FocusOverlayOrBoard()
    {
        if (IsOverlayOpen && Overlay.Visibility == Visibility.Visible)
        {
            OverlayPrimaryButton.Focus(FocusState.Programmatic);
        }
        else
        {
            Focus(FocusState.Programmatic);
        }
    }

    private void ShowOverlayNow()
    {
        if (!IsOverlayOpen)
        {
            return;
        }

        OverlayHideStoryboard.Stop();
        Overlay.Visibility = Visibility.Visible;
        OverlayShowStoryboard.Begin();
        OverlayPrimaryButton.Focus(FocusState.Programmatic);
    }

    private void FinishSlide()
    {
        if (_slide is { } slide)
        {
            _slide = null;
            slide.Stop();
        }

        Action? finish = _finishSlide;
        _finishSlide = null;
        finish?.Invoke();
    }

    private TileView AddTile(int id, int value, int cell)
    {
        TileView tile = new(id, value);
        (tile.Position.TranslateX, tile.Position.TranslateY) = PositionOf(cell);
        _tiles[id] = tile;
        TileLayer.Children.Add(tile);
        return tile;
    }

    private void RemoveTile(int id)
    {
        if (_tiles.Remove(id, out TileView? tile))
        {
            TileLayer.Children.Remove(tile);
        }
    }

    private static (double X, double Y) PositionOf(int cell) =>
        ((cell % GameEngine.Size) * CellStep, (cell / GameEngine.Size) * CellStep);

    private static void AddSlide(Storyboard storyboard, DependencyObject target, string property, double from, double to)
    {
        if (from == to)
        {
            return;
        }

        DoubleAnimation animation = new()
        {
            From = from,
            To = to,
            Duration = SlideDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    /// <summary>New tiles grow in from nothing with a slight overshoot.</summary>
    private static void Appear(TileView tile, TimeSpan delay)
    {
        tile.Opacity = 0;
        Storyboard storyboard = new() { BeginTime = delay };
        foreach (string property in (string[])["ScaleX", "ScaleY"])
        {
            DoubleAnimation grow = new()
            {
                From = 0.1,
                To = 1,
                Duration = AppearDuration,
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(grow, tile.Position);
            Storyboard.SetTargetProperty(grow, property);
            storyboard.Children.Add(grow);
        }

        DoubleAnimation fade = new() { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(90) };
        Storyboard.SetTarget(fade, tile);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) => tile.Opacity = 1;
        storyboard.Begin();
    }

    /// <summary>A merged tile swells and settles, the classic 2048 "pop".</summary>
    private static void Pop(TileView tile)
    {
        Canvas.SetZIndex(tile, 1);
        Storyboard storyboard = new();
        foreach (string property in (string[])["ScaleX", "ScaleY"])
        {
            DoubleAnimationUsingKeyFrames pop = new();
            pop.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
            pop.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(90),
                Value = 1.2,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            pop.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(220),
                Value = 1,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
            Storyboard.SetTarget(pop, tile.Position);
            Storyboard.SetTargetProperty(pop, property);
            storyboard.Children.Add(pop);
        }

        storyboard.Completed += (_, _) => Canvas.SetZIndex(tile, 0);
        storyboard.Begin();
    }

    private DoubleAnimationUsingKeyFrames BumpAnimation(string property, double distance)
    {
        DoubleAnimationUsingKeyFrames bump = new();
        bump.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(70),
            Value = distance,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        bump.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(220),
            Value = 0,
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        });
        Storyboard.SetTarget(bump, BumpTransform);
        Storyboard.SetTargetProperty(bump, property);
        return bump;
    }

    private void BoardRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsOverlayOpen)
        {
            return;
        }

        Focus(FocusState.Pointer);
        _pressPoint = e.GetCurrentPoint(BoardRoot).Position;
        BoardRoot.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void BoardRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPoint is not { } start)
        {
            return;
        }

        _pressPoint = null;
        BoardRoot.ReleasePointerCapture(e.Pointer);
        Point end = e.GetCurrentPoint(BoardRoot).Position;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < SwipeThreshold)
        {
            return;
        }

        MoveDirection direction = Math.Abs(dx) > Math.Abs(dy)
            ? dx > 0 ? MoveDirection.Right : MoveDirection.Left
            : dy > 0 ? MoveDirection.Down : MoveDirection.Up;
        Swiped?.Invoke(this, direction);
        e.Handled = true;
    }

    private void BoardRoot_PointerCanceled(object sender, PointerRoutedEventArgs e) => _pressPoint = null;

    private void OverlayPrimaryButton_Click(object sender, RoutedEventArgs e) =>
        OverlayChosen?.Invoke(this, _overlayIsWin ? OverlayChoice.KeepGoing : OverlayChoice.NewGame);

    private void OverlaySecondaryButton_Click(object sender, RoutedEventArgs e) =>
        OverlayChosen?.Invoke(this, _overlayIsWin ? OverlayChoice.NewGame : OverlayChoice.Undo);
}
