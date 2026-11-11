using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CardForgeStudio.DemoRunner;

/// <summary>ANSI palette, glyph sets and layout helpers shared by every console view in the demo.</summary>
public sealed class ConsoleStyle
{
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string Amber = "\u001b[38;5;214m";
    public const string Gold = "\u001b[1m\u001b[38;5;220m";
    public const string Cyan = "\u001b[38;5;45m";
    public const string Teal = "\u001b[38;5;37m";
    public const string Green = "\u001b[38;5;78m";
    public const string Red = "\u001b[38;5;203m";
    public const string Violet = "\u001b[38;5;177m";
    public const string Gray = "\u001b[38;5;245m";
    public const string White = "\u001b[38;5;255m";

    private static readonly Regex AnsiPattern = new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);

    private readonly bool useColor;

    /// <summary>Creates a style set; <paramref name="useColor"/> strips every escape code when false.</summary>
    public ConsoleStyle(bool useColor, bool useUnicode, int width)
    {
        this.useColor = useColor;
        Glyphs = useUnicode ? GlyphSet.Unicode : GlyphSet.Ascii;
        Width = width;
    }

    /// <summary>Glyphs used for borders, bullets and bars.</summary>
    public GlyphSet Glyphs { get; }

    /// <summary>Total layout width in columns.</summary>
    public int Width { get; }

    /// <summary>Text width available inside a <see cref="Box"/>.</summary>
    public int BoxTextWidth => Width - 8;

    /// <summary>Picks a layout width from the console window, or a fixed width when output is redirected.</summary>
    public static int DetectWidth()
    {
        if (Console.IsOutputRedirected) return 88;
        try { return Math.Clamp(Console.WindowWidth - 1, 72, 110); }
        catch (IOException) { return 88; }
    }

    /// <summary>Turns on ANSI escape processing for classic Windows consoles; a no-op elsewhere.</summary>
    public static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected) return;
        const int StdOutputHandle = -11;
        const uint VirtualTerminalProcessing = 0x0004;
        var handle = NativeMethods.GetStdHandle(StdOutputHandle);
        if (NativeMethods.GetConsoleMode(handle, out var mode)) NativeMethods.SetConsoleMode(handle, mode | VirtualTerminalProcessing);
    }

    /// <summary>Wraps text in an ANSI style when color is enabled.</summary>
    public string Paint(string text, string style) => useColor && text.Length > 0 ? style + text + Reset : text;

    /// <summary>Length of the text as displayed, ignoring escape codes.</summary>
    public static int Visible(string text) => AnsiPattern.Replace(text, string.Empty).Length;

    /// <summary>Pads on the right to a visible width.</summary>
    public static string PadRight(string text, int width) => text + new string(' ', Math.Max(0, width - Visible(text)));

    /// <summary>Centers text within a visible width.</summary>
    public static string Center(string text, int width)
    {
        var gap = Math.Max(0, width - Visible(text));
        return new string(' ', gap / 2) + text + new string(' ', gap - gap / 2);
    }

    /// <summary>Truncates plain text with an ellipsis so it fits the width.</summary>
    public string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + Glyphs.Ellipsis;

    /// <summary>Repeats a glyph.</summary>
    public static string Repeat(string glyph, int count) => count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(glyph, count));

    /// <summary>Draws a proportional bar such as ████░░░░.</summary>
    public string Bar(long value, long max, int cells, string fillStyle)
    {
        var ratio = max <= 0 ? 0 : Math.Clamp((double)value / max, 0, 1);
        var filled = (int)Math.Round(ratio * cells);
        if (value > 0 && filled == 0) filled = 1;
        return Paint(Repeat(Glyphs.BarFull, filled), fillStyle) + Paint(Repeat(Glyphs.BarEmpty, cells - filled), Gray);
    }

    /// <summary>Draws a full-width section rule with a title on the left and optional note on the right.</summary>
    public string Header(string title, string note = "")
    {
        var left = Glyphs.RuleHeavy + Glyphs.RuleHeavy + " ";
        var right = note.Length == 0 ? string.Empty : " " + note + " " + Glyphs.RuleHeavy + Glyphs.RuleHeavy;
        var fill = Math.Max(2, Width - 2 - left.Length - title.Length - 1 - right.Length);
        return "  " + Paint(left, Gray) + Paint(title, Bold + Amber) + Paint(" " + Repeat(Glyphs.RuleHeavy, fill), Gray)
            + (note.Length == 0 ? string.Empty : Paint(right, Gray));
    }

    /// <summary>Draws a bordered panel around pre-formatted lines.</summary>
    public IEnumerable<string> Box(IReadOnlyList<string> lines, string borderStyle, bool heavy)
    {
        var g = Glyphs;
        var (h, v, tl, tr, bl, br) = heavy
            ? (g.DoubleH, g.DoubleV, g.DoubleTL, g.DoubleTR, g.DoubleBL, g.DoubleBR)
            : (g.LightH, g.LightV, g.LightTL, g.LightTR, g.LightBL, g.LightBR);
        var inner = Width - 6;
        yield return "  " + Paint(tl + Repeat(h, inner) + tr, borderStyle);
        foreach (var line in lines)
        {
            yield return "  " + Paint(v, borderStyle) + " " + PadRight(line, inner - 2) + " " + Paint(v, borderStyle);
        }

        yield return "  " + Paint(bl + Repeat(h, inner) + br, borderStyle);
    }

    /// <summary>The characters used to draw the interface.</summary>
    public sealed class GlyphSet
    {
        public static readonly GlyphSet Unicode = new()
        {
            RuleHeavy = "━", LightH = "─", LightV = "│", LightTL = "┌", LightTR = "┐", LightBL = "└", LightBR = "┘",
            DoubleH = "═", DoubleV = "║", DoubleTL = "╔", DoubleTR = "╗", DoubleBL = "╚", DoubleBR = "╝",
            Rail = "┃", Arrow = "▸", Branch = "↳", Ok = "✔", Fail = "✖", Star = "★", Diamond = "◆", Spark = "✦",
            Advance = "»", BarFull = "█", BarEmpty = "░", Dot = "●", DotEmpty = "○", Sep = "·", Ellipsis = "…", Right = "→",
            LeftCard = "‹", RightCard = "›",
        };

        public static readonly GlyphSet Ascii = new()
        {
            RuleHeavy = "=", LightH = "-", LightV = "|", LightTL = "+", LightTR = "+", LightBL = "+", LightBR = "+",
            DoubleH = "=", DoubleV = "|", DoubleTL = "+", DoubleTR = "+", DoubleBL = "+", DoubleBR = "+",
            Rail = "|", Arrow = ">", Branch = "`-", Ok = "+", Fail = "x", Star = "*", Diamond = "*", Spark = "*",
            Advance = ">", BarFull = "#", BarEmpty = ".", Dot = "o", DotEmpty = ".", Sep = "|", Ellipsis = "~", Right = "->",
            LeftCard = "<", RightCard = ">",
        };

        public string RuleHeavy { get; init; } = "";
        public string LightH { get; init; } = "";
        public string LightV { get; init; } = "";
        public string LightTL { get; init; } = "";
        public string LightTR { get; init; } = "";
        public string LightBL { get; init; } = "";
        public string LightBR { get; init; } = "";
        public string DoubleH { get; init; } = "";
        public string DoubleV { get; init; } = "";
        public string DoubleTL { get; init; } = "";
        public string DoubleTR { get; init; } = "";
        public string DoubleBL { get; init; } = "";
        public string DoubleBR { get; init; } = "";
        public string Rail { get; init; } = "";
        public string Arrow { get; init; } = "";
        public string Branch { get; init; } = "";
        public string Ok { get; init; } = "";
        public string Fail { get; init; } = "";
        public string Star { get; init; } = "";
        public string Diamond { get; init; } = "";
        public string Spark { get; init; } = "";
        public string Advance { get; init; } = "";
        public string BarFull { get; init; } = "";
        public string BarEmpty { get; init; } = "";
        public string Dot { get; init; } = "";
        public string DotEmpty { get; init; } = "";
        public string Sep { get; init; } = "";
        public string Ellipsis { get; init; } = "";
        public string Right { get; init; } = "";
        public string LeftCard { get; init; } = "";
        public string RightCard { get; init; } = "";
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll")]
        public static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll")]
        public static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
