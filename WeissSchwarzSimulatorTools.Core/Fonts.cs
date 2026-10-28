// Fonts compiled into the program, shared by the window and the PDFs. They are embedded
// resources (see WeissSchwarzSimulatorTools.Core.csproj), so nothing needs to be installed next to the
// executable.

namespace WeissSchwarzSimulatorTools;

public static class Fonts
{
    public static readonly byte[] NotoSansRegular = Load("NotoSans-Regular.ttf");
    public static readonly byte[] NotoSansMedium = Load("NotoSans-Medium.ttf");

    private static byte[] Load(string name)
    {
        using var stream = typeof(Fonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded font {name}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
