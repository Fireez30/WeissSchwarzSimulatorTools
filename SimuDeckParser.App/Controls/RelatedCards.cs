// "Related cards": the cards whose effect text names a card, and the ones its text names, in a
// window of their own. Clicking one shows it in the detail pane of the screen on display.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SimuDeckParser.App.Controls;

public static class RelatedCards
{
    private static Window? _window;

    /// <summary>A card was clicked in the window: the detail pane on screen pins it.</summary>
    public static event Action<Card>? PinRequested;

    /// <summary>Code of the card pinned in the detail pane on screen, highlighted in the window.</summary>
    public static string? Pinned { get; set; }

    /// <summary>Shows the cards related to <paramref name="target"/>, in the window (opened if needed).</summary>
    public static void Open(Card target)
    {
        if (CardNames.Index is not { } names) return;
        var related = names.Related(target);
        if (_window is null)
        {
            _window = new Window
            {
                Title = "Related cards",
                Width = 780,
                Height = 620,
                Background = ThemeBrushes.Brush("SurfaceContainerHigh"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            _window.Closed += (_, _) => _window = null;
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is not null) _window.Show(owner);
            else _window.Show();
        }
        _window.Content = Build(target, related);
        _window.Activate();
    }

    public static void Close() => _window?.Close();

    private static Control Build(Card target, Related related)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var image = new Image { Width = 40, Height = 40 * CardImage.Ratio };
        Pictures.SetDecodeWidth(image, 80);
        Pictures.SetPath(image, target.Image);
        ToolTip.SetTip(image, new ToolTip { Content = CardViews.Preview(target), Classes = { "rich" } });
        header.Children.Add(new Border { Child = image, CornerRadius = new CornerRadius(4), ClipToBounds = true });
        header.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { CardViews.Text(target.Name, "titleMedium"), CardViews.Text(target.Key, "label muted") },
        });

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(header);
        body.Children.Add(CardViews.Text("Click a card to see its details in the side panel.", "bodySmall muted"));
        foreach (var (title, cards, empty) in new[]
        {
            ("Named in its text", related.Named, "Its text names no other card."),
            ("Naming it", related.Naming, "No card of its series names it."),
        })
        {
            body.Children.Add(SectionHeader(title, cards.Count));
            if (cards.Count == 0) body.Children.Add(CardViews.Text(empty, "bodySmall muted"));
            var grid = new WrapPanel();
            foreach (var card in cards) grid.Children.Add(Thumbnail(card));
            body.Children.Add(grid);
        }
        return new ScrollViewer { Content = body, Padding = new Thickness(20) };
    }

    public static Control SectionHeader(string title, int? count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
        row.Children.Add(CardViews.Text(title, "label muted"));
        if (count is { } n)
            row.Children.Add(new Border { Classes = { "chip" }, Padding = new Thickness(8, 0), Child = CardViews.Text(n.ToString(), "label muted", 12) });
        return row;
    }

    /// <summary>Picture and name of a related card; hover previews it, click shows it in the detail pane.</summary>
    private static Control Thumbnail(Card card)
    {
        const double width = 104;
        var image = new Image { Width = width, Height = width * CardImage.Ratio };
        Pictures.SetDecodeWidth(image, 200);
        Pictures.SetPath(image, card.Image);
        var content = new StackPanel
        {
            Width = width,
            Children =
            {
                new Border { Child = image, CornerRadius = new CornerRadius(6), ClipToBounds = true },
                new TextBlock { Text = card.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = card.Key, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "muted" } },
            },
        };
        var frame = new Border
        {
            Child = content,
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(3),
            BorderBrush = Pinned == card.Key ? ThemeBrushes.Brush("Primary") : Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(frame, new ToolTip { Content = CardViews.Preview(card), Classes = { "rich" } });
        frame.PointerEntered += (_, _) =>
        {
            if (Pinned != card.Key) frame.BorderBrush = ThemeBrushes.Brush("Outline");
        };
        frame.PointerExited += (_, _) => frame.BorderBrush = Pinned == card.Key ? ThemeBrushes.Brush("Primary") : Brushes.Transparent;
        frame.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            PinRequested?.Invoke(card);
            frame.BorderBrush = ThemeBrushes.Brush("Primary");
        };
        return frame;
    }
}
