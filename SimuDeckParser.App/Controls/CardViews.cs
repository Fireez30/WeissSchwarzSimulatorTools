// Showing a card: its summary (name, stats, traits), its effect text with icons and hoverable
// card names, and a preview (picture, summary and text) for tooltips.
//
// They are built in code rather than XAML because their content depends on the card: one chip
// per stat, one icon per trigger, one inline per piece of effect text.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace SimuDeckParser.App.Controls;

/// <summary>The loaded cards' names, so quoted names in effect text show their card on hover.</summary>
public static class CardNames
{
    public static NameIndex? Index { get; set; }
}

/// <summary>Base of the controls that show one card: rebuilt when <see cref="Card"/> changes.</summary>
public abstract class CardControl : ContentControl
{
    public static readonly StyledProperty<Card?> CardProperty =
        AvaloniaProperty.Register<CardControl, Card?>(nameof(Card));

    public Card? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty) Content = Card is null ? null : Build(Card);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Colours come from the theme: rebuild when it changes.
        if (Application.Current is { } app) app.ActualThemeVariantChanged += Rebuild;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= Rebuild;
    }

    private void Rebuild(object? sender, EventArgs e) => Content = Card is null ? null : Build(Card);

    protected abstract Control Build(Card card);
}

/// <summary>Name, code, stats as chips and traits.</summary>
public sealed class CardSummaryView : CardControl
{
    public static readonly StyledProperty<bool> CopyButtonProperty =
        AvaloniaProperty.Register<CardSummaryView, bool>(nameof(CopyButton), true);

    public bool CopyButton
    {
        get => GetValue(CopyButtonProperty);
        set => SetValue(CopyButtonProperty, value);
    }

    protected override Control Build(Card card) => CardViews.Summary(card, CopyButton);
}

/// <summary>Effect text, with keyword icons and (with <see cref="Links"/>) hoverable card names.</summary>
public sealed class EffectTextView : CardControl
{
    public static readonly StyledProperty<bool> LinksProperty =
        AvaloniaProperty.Register<EffectTextView, bool>(nameof(Links), true);

    public bool Links
    {
        get => GetValue(LinksProperty);
        set => SetValue(LinksProperty, value);
    }

    protected override Control Build(Card card) => CardViews.EffectText(card, Links);
}

/// <summary>Picture, summary and text, with nothing clickable: for tooltips.</summary>
public sealed class CardPreviewView : CardControl
{
    protected override Control Build(Card card) => CardViews.Preview(card);
}

public static class CardViews
{
    public static TextBlock Text(string text, string classes = "", double? size = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) block.Classes.Add(c);
        if (size is { } s) block.FontSize = s;
        return block;
    }

    public static MaterialIcon Icon(MaterialIconKind kind, double size = 20, IBrush? brush = null)
    {
        var icon = new MaterialIcon { Kind = kind, Width = size, Height = size };
        if (brush is not null) icon.Foreground = brush;
        return icon;
    }

    /// <summary>A static chip, with a leading colour dot when <paramref name="dot"/> is set.</summary>
    public static Control InfoChip(string text, IBrush? dot = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (dot is not null)
            row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = dot, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Text(text, "label"));
        var chip = new Border { Child = row, Margin = new Thickness(0, 0, 6, 6) };
        chip.Classes.Add("chip");
        return chip;
    }

    public static Control Summary(Card card, bool copyButton)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Text(card.Name, "title"));
        var code = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        code.Children.Add(new SelectableTextBlock { Text = card.Key, FontWeight = FontWeight.Medium, FontSize = 13, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        if (copyButton)
        {
            var copy = new Button { Content = Icon(MaterialIconKind.ContentCopy, 16), Classes = { "icon", "small" } };
            ToolTip.SetTip(copy, "Copy card code");
            copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(card.Key);
            };
            code.Children.Add(copy);
        }
        panel.Children.Add(code);

        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(InfoChip(card.Color.Label(), CardColors.Brush(card.Color)));
        chips.Children.Add(InfoChip(card.Type.Label()));
        if (card.Type != CardType.Climax)
        {
            chips.Children.Add(InfoChip($"Level {card.Level}"));
            chips.Children.Add(InfoChip($"Cost {card.Cost}"));
        }
        if (card.Type == CardType.Character) chips.Children.Add(InfoChip($"Power {card.Power}"));
        panel.Children.Add(chips);

        if (card.Triggers.Count > 0 || card.SoulCount > 0)
        {
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (card.Triggers.Count > 0)
            {
                icons.Children.Add(Text("Triggers", "label muted"));
                foreach (var t in card.Triggers)
                {
                    var image = new Image { Source = Pictures.TriggerIcon(t), Width = 16, Height = 20 };
                    ToolTip.SetTip(image, t.Label());
                    icons.Children.Add(image);
                }
                icons.Children.Add(new Control { Width = 12 });
            }
            if (card.SoulCount > 0)
            {
                icons.Children.Add(Text("Soul", "label muted"));
                for (var i = 0; i < card.SoulCount; i++)
                    icons.Children.Add(new Image { Source = Pictures.Soul, Width = 16, Height = 20 });
            }
            panel.Children.Add(icons);
        }

        if (card.Traits.Count > 0)
        {
            var traits = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var t in card.Traits)
            {
                var chip = new Border { Child = Text(t, "label muted"), Margin = new Thickness(0, 0, 6, 6) };
                chip.Classes.Add("outlinedChip");
                traits.Children.Add(chip);
            }
            panel.Children.Add(traits);
        }
        return panel;
    }

    /// <summary>Hover preview: picture, summary and text, with nothing clickable.</summary>
    public static Control Preview(Card card)
    {
        var panel = new StackPanel { Spacing = 6, MaxWidth = 300 };
        var image = new Image { Width = 260, Height = 260 * CardImage.Ratio, HorizontalAlignment = HorizontalAlignment.Left };
        Pictures.SetDecodeWidth(image, 300);
        Pictures.SetPath(image, card.Image);
        panel.Children.Add(new Border { Child = image, CornerRadius = new CornerRadius(10), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(Summary(card, false));
        panel.Children.Add(EffectText(card, false));
        return panel;
    }

    /// <summary>Keyword -> icon, as the original Qt tool did; other keywords (TextHelpers.Keywords) become bold words.</summary>
    private static readonly (string Pattern, string Image, double Width)[] KeywordIcons =
    [
        ("Auto:", "auto.jpg", 28),
        ("Cont:", "cont.jpg", 28),
        ("Act:", "act.jpg", 28),
        ("CxCombo", "ccx.png", 52),
    ];

    /// <summary>
    /// The card's effect text, in a rounded box. With <paramref name="links"/>, quoted card names
    /// preview their card on hover.
    /// </summary>
    public static Control EffectText(Card card, bool links)
    {
        var lines = TextHelpers.Lines(card.Text).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return new Control();
        var names = links ? CardNames.Index : null;
        var panel = new StackPanel { Spacing = 6 };
        foreach (var line in lines)
        {
            var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
            block.Inlines = TextLine(line, card, names);
            panel.Children.Add(block);
        }
        var box = new Border { Child = panel, Margin = new Thickness(0, 8, 0, 0) };
        box.Classes.Add("textBox");
        return box;
    }

    /// <summary>
    /// One line of card text: rich-text tags, then keywords turned into icons or bold words.
    /// Quoted names of other cards are highlighted and preview that card on hover.
    /// </summary>
    private static InlineCollection TextLine(string line, Card card, NameIndex? names)
    {
        var inlines = new InlineCollection();
        foreach (var rich in TextHelpers.ParseRich(line))
        {
            switch (rich)
            {
                case Rich.Sprite { Name: var name }:
                    inlines.AddRange(Sprite(name));
                    break;
                case Rich.Text { Value: var text, Style: var style }:
                    if (names is null)
                    {
                        Keywords(inlines, text, style);
                        break;
                    }
                    foreach (var span in TextHelpers.SplitNames(text, n => names.Get(n, card) is not null))
                    {
                        if (span.Name is { } n && names.Get(n, card) is { } target)
                            inlines.Add(new InlineUIContainer(CardLink(span.Span, target, style)) { BaselineAlignment = BaselineAlignment.TextBottom });
                        else
                            Keywords(inlines, span.Span, style);
                    }
                    break;
            }
        }
        return inlines;
    }

    private static Run Styled(string text, TextStyle style)
    {
        var run = new Run(text);
        if (style.Bold) run.FontWeight = FontWeight.SemiBold;
        if (style.Italic) run.FontStyle = FontStyle.Italic;
        if (style.Highlight) run.Foreground = ThemeBrushes.Brush("Error");
        if (style.Strike) run.TextDecorations = TextDecorations.Strikethrough;
        return run;
    }

    /// <summary>Text, with the keywords it contains turned into icons (Auto:, CxCombo…) or bold words (ENCORE -> Encore).</summary>
    private static void Keywords(InlineCollection inlines, string text, TextStyle style)
    {
        while (text.Length > 0)
        {
            var next = KeywordIcons.Select(k => (k.Pattern, Image: (string?)k.Image, k.Width, Word: (string?)null))
                .Concat(TextHelpers.Keywords.Select(k => (Pattern: k.Upper, Image: (string?)null, Width: 0.0, Word: (string?)k.Word)))
                .Select(k => (Index: text.IndexOf(k.Pattern, StringComparison.Ordinal), k))
                .Where(k => k.Index >= 0)
                .OrderBy(k => k.Index).ThenByDescending(k => k.k.Pattern.Length)
                .FirstOrDefault();
            if (next.k.Pattern is null)
            {
                inlines.Add(Styled(text, style));
                return;
            }
            if (next.Index > 0) inlines.Add(Styled(text[..next.Index], style));
            if (next.k.Image is { } file)
            {
                var image = new Image { Source = Pictures.Asset(file), Width = next.k.Width, Height = 14 };
                var icon = new Border { Child = image, CornerRadius = new CornerRadius(3), ClipToBounds = true, Margin = new Thickness(0, 0, 4, 0) };
                inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
            }
            else
            {
                inlines.Add(Styled(next.k.Word!, style with { Bold = true }));
            }
            text = text[(next.Index + next.k.Pattern.Length)..];
        }
    }

    /// <summary>&lt;sprite name="…"/&gt;: stock costs, card states, trigger icons, keywords.</summary>
    private static IEnumerable<Inline> Sprite(string name)
    {
        if (TextHelpers.SpriteCost(name) is { } cost)
        {
            var circle = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = ThemeBrushes.Brush("Primary"),
                Child = new TextBlock { Text = cost.ToString(), FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeBrushes.Brush("OnPrimary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ToolTip.SetTip(circle, $"Pay {cost} stock");
            yield return new InlineUIContainer(circle) { BaselineAlignment = BaselineAlignment.Center };
            yield break;
        }
        if (TextHelpers.SpriteTrigger(name) is { } t)
        {
            var image = new Image { Source = Pictures.TriggerIcon(t), Width = 14, Height = 17 };
            ToolTip.SetTip(image, t.Label());
            yield return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
            yield break;
        }
        if (name == "alarm")
        {
            // Always followed by the ALARM keyword itself.
            var icon = Icon(MaterialIconKind.Alarm, 16, ThemeBrushes.Brush("Primary"));
            ToolTip.SetTip(icon, "Alarm");
            yield return new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center };
            yield return new Run(" ");
            yield break;
        }
        var chip = new Border
        {
            Background = ThemeBrushes.Brush("SecondaryContainer"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0),
            Child = new TextBlock { Text = TextHelpers.SpriteWord(name), FontSize = 13, FontWeight = FontWeight.Medium, Foreground = ThemeBrushes.Brush("OnSecondaryContainer") },
        };
        yield return new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>
    /// A quoted card name: highlighted; hovering it opens a panel with the card, which stays open
    /// while the pointer is over the name or the panel.
    /// </summary>
    private static Control CardLink(string span, Card target, TextStyle style)
    {
        var link = new TextBlock
        {
            Text = span,
            Foreground = ThemeBrushes.Brush("Primary"),
            TextDecorations = TextDecorations.Underline,
            FontWeight = style.Bold ? FontWeight.SemiBold : FontWeight.Normal,
            Cursor = new Cursor(StandardCursorType.Help),
        };
        LinkPanel.Attach(link, target);
        return link;
    }
}

/// <summary>
/// The panel a card link opens: beside the name, with the card and its related cards button.
/// It closes once the pointer has been off both the link and the panel for a moment (time to
/// cross the gap between them).
/// </summary>
internal static class LinkPanel
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);
    private static Popup? _popup;
    private static Control? _link;
    private static DispatcherTimer? _closer;

    public static void Attach(Control link, Card target)
    {
        link.PointerEntered += (_, _) => Open(link, target);
        link.PointerExited += (_, _) => CloseSoon();
        link.DetachedFromVisualTree += (_, _) =>
        {
            if (_link == link) Close();
        };
    }

    private static void Open(Control link, Card target)
    {
        _closer?.Stop();
        if (_link == link && _popup is { IsOpen: true }) return;
        Close();
        var related = new Button { Classes = { "tonal" }, Margin = new Thickness(0, 0, 0, 8) };
        related.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { CardViews.Icon(MaterialIconKind.Hub, 18), new TextBlock { Text = "Related cards" } },
        };
        related.Click += (_, _) =>
        {
            Close();
            RelatedCards.Open(target);
        };
        var content = new Border
        {
            Background = ThemeBrushes.Brush("SurfaceContainerHigh"),
            BorderBrush = ThemeBrushes.Brush("OutlineVariant"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            BoxShadow = BoxShadows.Parse("0 3 12 0 #40000000"),
            Child = new ScrollViewer
            {
                MaxHeight = 640,
                Content = new StackPanel { Width = 300, Children = { related, CardViews.Preview(target) } },
            },
        };
        content.PointerEntered += (_, _) => _closer?.Stop();
        content.PointerExited += (_, _) => CloseSoon();
        _popup = new Popup
        {
            Child = content,
            PlacementTarget = link,
            Placement = PlacementMode.RightEdgeAlignedTop,
            HorizontalOffset = 4,
            IsLightDismissEnabled = false,
        };
        ((ISetLogicalParent)_popup).SetParent(link);
        _link = link;
        _popup.IsOpen = true;
    }

    private static void CloseSoon()
    {
        _closer ??= new DispatcherTimer(Grace, DispatcherPriority.Normal, (_, _) => Close());
        _closer.Stop();
        _closer.Start();
    }

    private static void Close()
    {
        _closer?.Stop();
        if (_popup is not null)
        {
            _popup.IsOpen = false;
            ((ISetLogicalParent)_popup).SetParent(null);
        }
        _popup = null;
        _link = null;
    }
}
