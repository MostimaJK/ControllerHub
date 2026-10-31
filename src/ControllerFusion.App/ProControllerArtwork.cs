// Controller shell geometry and button placement adapted from NS2ProCon,
// ErenYaeger999, MIT. See Assets/NS2ProCon-LICENSE.txt and ARTWORK-NOTICE.md.
// WPF DrawingContext rendering and layout adaptation by ControllerFusion.
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ControllerFusion;

internal static class ProControllerArtwork
{
    static Brush Solid(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + hex));
    static Brush Gradient(string a, string b) => new LinearGradientBrush(((SolidColorBrush)Solid(a)).Color, ((SolidColorBrush)Solid(b)).Color, 90);
    static readonly Brush Key = Gradient("41484C", "252C30");
    static readonly Brush Light = Solid("D4DCDD");

    public static void Draw(DrawingContext dc, double width, double height, bool showRear)
    {
        double bottom = showRear ? 840 : 700;
        double scale = Math.Min((width - 24) / 975, (height - 8) / bottom);
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((width - 975 * scale) / 2, (height - bottom * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawEllipse(Solid("EEF0F5"), null, new Point(488, 665), 340, 24);
        Shape(dc, "M 146 111 C 167 61 230 28 305 26 C 339 26 360 43 377 61 L 352 101 Z", Gradient("D3D9DA", "9CA7AC"));
        Shape(dc, "M 599 61 C 616 43 637 26 671 26 C 746 28 809 61 830 111 L 624 101 Z", Gradient("D3D9DA", "9CA7AC"));
        Shape(dc, "M 147 101 C 185 63 273 39 381 42 L 594 42 C 702 39 790 63 828 101 C 865 149 883 280 899 398 L 925 534 C 938 607 911 666 865 672 C 823 678 790 639 759 586 L 713 506 C 705 491 691 486 670 486 L 305 486 C 284 486 270 491 262 506 L 216 586 C 185 639 152 678 110 672 C 64 666 37 607 50 534 L 76 398 C 92 280 110 149 147 101 Z", Gradient("3E474D", "20272B"), "141C21", 2);
        Shape(dc, "M 158 112 C 220 72 340 64 488 65 C 636 64 756 72 818 112 C 853 179 864 298 840 373 C 822 430 785 460 728 461 L 248 461 C 191 460 154 430 136 373 C 112 298 123 179 158 112 Z", Gradient("41494E", "2A3237"), "1B2328", 2);
        Stick(dc, 250, 224, 80); Stick(dc, 601, 351, 77);
        foreach (var (x, y, label) in new[] { (715d, 163d, "X"), (645d, 224d, "Y"), (785d, 224d, "A"), (715d, 285d, "B") }) RoundKey(dc, x, y, 35, label);
        RoundKey(dc, 382, 153, 21, "−", 32); RoundKey(dc, 593, 153, 21, "+", 30);
        RoundKey(dc, 541, 224, 28, "⌂", 38);
        dc.DrawRoundedRectangle(Key, new Pen(Solid("141D23"), 2), new Rect(414, 205, 38, 38), 7, 7); Label(dc, "○", 433, 224, 22, Light);
        dc.DrawRoundedRectangle(Key, new Pen(Solid("141D23"), 2), new Rect(469, 397, 36, 36), 7, 7); Label(dc, "C", 487, 415, 22, Light);
        Shape(dc, "M 338 280 Q 338 274 344 274 L 375 274 Q 381 274 381 280 L 381 318 L 420 318 Q 426 318 426 324 L 426 355 Q 426 361 420 361 L 381 361 L 381 400 Q 381 406 375 406 L 344 406 Q 338 406 338 400 L 338 361 L 299 361 Q 293 361 293 355 L 293 324 Q 293 318 299 318 L 338 318 Z", Key, "121A1F", 5);
        Label(dc, "▲", 359, 296, 13, Light); Label(dc, "▼", 359, 382, 13, Light); Label(dc, "◀", 315, 339, 13, Light); Label(dc, "▶", 404, 339, 13, Light);
        // Model label is our own typography; Nintendo logo assets are not copied.
        Label(dc, "PRO  /  2", 488, 106, 23, Solid("AAB5BD"));
        if (showRear)
        {
            RearKey(dc, 365, 750, "GL"); RearKey(dc, 610, 750, "GR");
            Label(dc, "背鍵位置示意", 488, 815, 25, Solid("636E80"));
        }
        dc.Pop(); dc.Pop();
    }

    static void Shape(DrawingContext dc, string path, Brush fill, string? stroke = null, double weight = 1) => dc.DrawGeometry(fill, stroke == null ? null : new Pen(Solid(stroke), weight), Geometry.Parse(path));
    static void Stick(DrawingContext dc, double x, double y, double radius)
    {
        dc.DrawEllipse(Gradient("20282D", "171F24"), new Pen(Solid("4A555D"), 2), new Point(x, y), radius, radius);
        dc.DrawEllipse(Gradient("67737A", "252F35"), new Pen(Solid("859098"), 2), new Point(x, y), radius * .71, radius * .71);
        dc.DrawEllipse(Gradient("3B474F", "202A31"), new Pen(Solid("151E24"), 3), new Point(x, y), radius * .64, radius * .64);
        dc.DrawEllipse(Gradient("3E4A52", "2D3840"), new Pen(Solid("52616A"), 1), new Point(x, y), radius * .54, radius * .54);
    }
    static void RoundKey(DrawingContext dc, double x, double y, double radius, string label, double font = 30)
    {
        dc.DrawEllipse(Solid("172025"), null, new Point(x, y + 3), radius + 1, radius + 1);
        dc.DrawEllipse(Key, new Pen(Solid("131C22"), 2), new Point(x, y), radius, radius);
        Label(dc, label, x, y, font, Light);
    }
    static void RearKey(DrawingContext dc, double x, double y, string label)
    {
        dc.DrawRoundedRectangle(Solid("41494E"), new Pen(Solid("242C31"), 2), new Rect(x - 80, y - 32, 160, 64), 18, 18); Label(dc, label, x, y, 30, Brushes.White);
    }
    static void Label(DrawingContext dc, string value, double x, double y, double size, Brush ink)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, ink, 1);
        dc.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }
}
