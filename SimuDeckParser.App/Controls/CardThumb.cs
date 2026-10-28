using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SimuDeckParser.App.Controls;

/// <summary>
/// A card picture: an outline when hovered or selected, a badge with its copies, and commands
/// (taking the card) for hover, click and right-click.
/// </summary>
public sealed class CardThumb : Panel
{
    public static readonly StyledProperty<Card?> CardProperty = AvaloniaProperty.Register<CardThumb, Card?>(nameof(Card));
    public static readonly StyledProperty<bool> SelectedProperty = AvaloniaProperty.Register<CardThumb, bool>(nameof(Selected));
    public static readonly StyledProperty<int> CopiesProperty = AvaloniaProperty.Register<CardThumb, int>(nameof(Copies));
    public static readonly StyledProperty<int> DecodeWidthProperty = AvaloniaProperty.Register<CardThumb, int>(nameof(DecodeWidth), 240);
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<CardThumb, double>(nameof(Radius), 8);
    public static readonly StyledProperty<ICommand?> HoverCommandProperty = AvaloniaProperty.Register<CardThumb, ICommand?>(nameof(HoverCommand));
    public static readonly StyledProperty<ICommand?> ClickCommandProperty = AvaloniaProperty.Register<CardThumb, ICommand?>(nameof(ClickCommand));
    public static readonly StyledProperty<ICommand?> RightClickCommandProperty = AvaloniaProperty.Register<CardThumb, ICommand?>(nameof(RightClickCommand));

    public Card? Card { get => GetValue(CardProperty); set => SetValue(CardProperty, value); }
    public bool Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    /// <summary>Shown in a badge when more than 0.</summary>
    public int Copies { get => GetValue(CopiesProperty); set => SetValue(CopiesProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public ICommand? HoverCommand { get => GetValue(HoverCommandProperty); set => SetValue(HoverCommandProperty, value); }
    public ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }
    public ICommand? RightClickCommand { get => GetValue(RightClickCommandProperty); set => SetValue(RightClickCommandProperty, value); }

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _frame = new() { ClipToBounds = true };
    private readonly Border _outline = new() { IsHitTestVisible = false };
    private readonly Border _badge = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), IsVisible = false };
    private readonly TextBlock _badgeText = new();
    private bool _hovered;

    public CardThumb()
    {
        _frame.Child = _image;
        _badge.Classes.Add("badge");
        _badge.Child = _badgeText;
        Children.Add(_frame);
        Children.Add(_outline);
        Children.Add(_badge);
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        UpdateRadius();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty || change.Property == DecodeWidthProperty)
        {
            Pictures.SetDecodeWidth(_image, DecodeWidth);
            Pictures.SetPath(_image, Card?.Image);
        }
        else if (change.Property == SelectedProperty) UpdateOutline();
        else if (change.Property == CopiesProperty)
        {
            _badge.IsVisible = Copies > 0;
            _badgeText.Text = Copies.ToString();
        }
        else if (change.Property == RadiusProperty) UpdateRadius();
    }

    private void UpdateRadius()
    {
        _frame.CornerRadius = new CornerRadius(Radius);
        _outline.CornerRadius = new CornerRadius(Radius + 2);
        _outline.Margin = new Thickness(-3);
    }

    private void UpdateOutline()
    {
        if (Selected)
        {
            _outline.BorderThickness = new Thickness(3);
            _outline.BorderBrush = ThemeBrushes.Brush("Primary");
        }
        else if (_hovered)
        {
            _outline.BorderThickness = new Thickness(2);
            _outline.BorderBrush = ThemeBrushes.Brush("Outline");
        }
        else
        {
            _outline.BorderBrush = null;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        UpdateOutline();
        if (Card is not null && HoverCommand?.CanExecute(Card) == true) HoverCommand.Execute(Card);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        UpdateOutline();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Card is null || !IsPointerOver) return;
        var command = e.InitialPressMouseButton switch
        {
            MouseButton.Left => ClickCommand,
            MouseButton.Right => RightClickCommand,
            _ => null,
        };
        if (command?.CanExecute(Card) != true) return;
        command.Execute(Card);
        e.Handled = true;
    }
}
