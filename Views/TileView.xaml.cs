using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tray2048.Views;

/// <summary>One numbered tile. Its colors and text size come from a visual state per value.</summary>
public sealed partial class TileView : UserControl
{
    public TileView(int id, int value)
    {
        InitializeComponent();
        Id = id;
        Value = value;
        Label.Text = value.ToString(CultureInfo.CurrentCulture);
        AutomationProperties.SetName(this, Label.Text);

        // Apply now so a tile never draws a blank frame; retry on Loaded if the tree wasn't ready.
        if (!VisualStateManager.GoToState(this, StateFor(value), false))
        {
            Loaded += (_, _) => VisualStateManager.GoToState(this, StateFor(value), false);
        }
    }

    public int Id { get; }

    public int Value { get; }

    public CompositeTransform Position => Transform;

    private static string StateFor(int value) => value <= 4096 ? $"Value{value}" : "ValueSuper";
}
