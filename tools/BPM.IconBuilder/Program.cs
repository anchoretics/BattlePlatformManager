using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// 从与界面相同的矢量源生成 ICO，不依赖外部图片、字体图标包或绘图软件。
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: BPM.IconBuilder <Icons.xaml> <BPM.ico>");
        using var input = File.OpenRead(args[0]);
        var icons = (ResourceDictionary)XamlReader.Load(input);
        var logo = ((DrawingImage)icons["BPMLogo"]).Drawing;
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = sizes.Select(size => Encode(Draw(size, size, drawing =>
        {
            drawing.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            drawing.DrawDrawing(logo);
            drawing.Pop();
        }))).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        using (var output = new BinaryWriter(File.Create(args[1])))
        {
            output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
            var offset = 6 + 16 * sizes.Length;
            for (var index = 0; index < sizes.Length; index++)
            {
                output.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                output.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
                output.Write(frames[index].Length); output.Write(offset); offset += frames[index].Length;
            }
            foreach (var frame in frames) output.Write(frame);
        }
        var preview = Draw(720, 260, drawing =>
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 247, 251)), null, new Rect(0, 0, 720, 260));
            drawing.PushTransform(new TranslateTransform(24, 26));
            drawing.PushTransform(new ScaleTransform(2, 2)); drawing.DrawDrawing(logo); drawing.Pop(); drawing.Pop();
            var keys = new[] { "Add", "Edit", "Delete", "Shortcut", "Import", "Export", "Play", "Retry", "Help", "Folder", "Save", "Close", "User", "Shield" };
            for (var index = 0; index < keys.Length; index++)
            {
                var x = 190 + index % 7 * 72; var y = 36 + index / 7 * 104;
                drawing.PushTransform(new TranslateTransform(x, y));
                drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(33, 59, 92)), 1.8)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, (Geometry)icons["Icon" + keys[index]]);
                drawing.Pop();
                drawing.DrawText(new FormattedText(keys[index], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Brushes.SlateGray, 1), new Point(x - 4, y + 34));
            }
        });
        File.WriteAllBytes(Path.ChangeExtension(args[1], ".preview.png"), Encode(preview));
        Console.WriteLine($"Generated {sizes.Length} icon sizes and preview.");
    }

    private static RenderTargetBitmap Draw(int width, int height, Action<DrawingContext> paint)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) paint(drawing);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
