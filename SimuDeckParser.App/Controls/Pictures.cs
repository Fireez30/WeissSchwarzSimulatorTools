// Pictures: the icons compiled into the program, and the simulator's pictures (card artwork,
// sleeves), read in the background and kept in a small cache.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SimuDeckParser.App.Controls;

public static class Pictures
{
    private static readonly Dictionary<string, Bitmap> Embedded = [];

    /// <summary>An image of Assets/Images, e.g. <c>soul.png</c>.</summary>
    public static Bitmap Asset(string file)
    {
        if (!Embedded.TryGetValue(file, out var bitmap))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://SimuDeckParser/Assets/Images/{file}"));
            Embedded[file] = bitmap = new Bitmap(stream);
        }
        return bitmap;
    }

    public static Bitmap EmptyCard => Asset("emptycard.jpg");
    public static Bitmap Soul => Asset("soul.png");

    /// <summary>Note: gate.png draws a door and door.png a gate, hence the apparent swap.</summary>
    public static Bitmap TriggerIcon(Trigger t) => Asset(t switch
    {
        Trigger.Bag => "goldbag.png",
        Trigger.Bar => "goldbar.png",
        Trigger.Book => "book.png",
        Trigger.Burn => "shot.png",
        Trigger.Choice => "switch.png",
        Trigger.Pant => "door.png",
        Trigger.Salvage => "gate.png",
        Trigger.Soul => "soul.png",
        Trigger.Standby => "standby.png",
        _ => "bounce.png",
    });

    /// <summary>Pictures read from disk, by path and width; the oldest are dropped past <see cref="CacheSize"/>.</summary>
    private const int CacheSize = 600;
    private static readonly Dictionary<(string, int), LinkedListNode<(string, int, Bitmap)>> Cache = [];
    private static readonly LinkedList<(string, int, Bitmap)> Recent = [];
    private static readonly object CacheLock = new();

    private static Bitmap? Cached(string path, int width)
    {
        lock (CacheLock)
        {
            if (!Cache.TryGetValue((path, width), out var node)) return null;
            Recent.Remove(node);
            Recent.AddFirst(node);
            return node.Value.Item3;
        }
    }

    private static void Keep(string path, int width, Bitmap bitmap)
    {
        lock (CacheLock)
        {
            if (Cache.ContainsKey((path, width))) return;
            Cache[(path, width)] = Recent.AddFirst((path, width, bitmap));
            // Dropped pictures are left to the garbage collector: an Image may still show one.
            while (Recent.Count > CacheSize)
            {
                var (p, w, _) = Recent.Last!.Value;
                Recent.RemoveLast();
                Cache.Remove((p, w));
            }
        }
    }

    /// <summary>Forgets the cached pictures (e.g. after a sleeve was replaced on disk).</summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
            Recent.Clear();
        }
    }

    /// <summary>The picture at <paramref name="path"/>, scaled down to <paramref name="width"/> pixels; null when it cannot be read.</summary>
    public static async Task<Bitmap?> LoadAsync(string path, int width)
    {
        if (Cached(path, width) is { } hit) return hit;
        var bitmap = await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                return width > 0 ? Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        });
        if (bitmap is not null) Keep(path, width, bitmap);
        return bitmap;
    }

    // Attached properties: <Image ctl:Pictures.Path="{Binding Image}" ctl:Pictures.DecodeWidth="300" />
    // shows the picture at Path, read in the background, with the empty card meanwhile (or when
    // Path is null or unreadable, unless Placeholder is false).

    public static readonly AttachedProperty<string?> PathProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Path", typeof(Pictures));

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<Image, int>("DecodeWidth", typeof(Pictures), 320);

    public static readonly AttachedProperty<bool> PlaceholderProperty =
        AvaloniaProperty.RegisterAttached<Image, bool>("Placeholder", typeof(Pictures), true);

    public static string? GetPath(Image image) => image.GetValue(PathProperty);
    public static void SetPath(Image image, string? value) => image.SetValue(PathProperty, value);
    public static int GetDecodeWidth(Image image) => image.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(Image image, int value) => image.SetValue(DecodeWidthProperty, value);
    public static bool GetPlaceholder(Image image) => image.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(Image image, bool value) => image.SetValue(PlaceholderProperty, value);

    static Pictures()
    {
        PathProperty.Changed.AddClassHandler<Image>((image, _) => Show(image));
        DecodeWidthProperty.Changed.AddClassHandler<Image>((image, _) => Show(image));
    }

    private static async void Show(Image image)
    {
        var path = GetPath(image);
        var width = GetDecodeWidth(image);
        IImage? placeholder = GetPlaceholder(image) ? EmptyCard : null;
        if (path is null)
        {
            image.Source = placeholder;
            return;
        }
        if (Cached(path, width) is { } hit)
        {
            image.Source = hit;
            return;
        }
        image.Source = placeholder;
        var bitmap = await LoadAsync(path, width);
        // The image may show another card by now (recycled list rows).
        if (GetPath(image) == path && GetDecodeWidth(image) == width)
            image.Source = bitmap ?? placeholder;
    }
}
