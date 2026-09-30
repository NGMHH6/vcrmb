using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

// 将本项目简洁的 SVG 几何源导出为 PNG 和多尺寸 ICO，不依赖外部图形库或字体。
internal static class BuildBrandAssets
{
    [STAThread]
    private static int Main(string[] args)
    {
        XmlDocument document = new XmlDocument { XmlResolver = null }; document.Load(args[0]);
        DrawingGroup drawing = new DrawingGroup();
        using (DrawingContext context = drawing.Open())
        {
            foreach (XmlElement element in document.DocumentElement.ChildNodes.OfType<XmlElement>())
            {
                if (element.LocalName == "title") continue;
                Brush fill = BrushValue(element.GetAttribute("fill"));
                Pen stroke = null;
                if (element.HasAttribute("stroke"))
                    stroke = new Pen(BrushValue(element.GetAttribute("stroke")), Number(element, "stroke-width"))
                        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                if (element.LocalName == "rect")
                    context.DrawRoundedRectangle(fill, stroke, new Rect(Number(element, "x"), Number(element, "y"),
                        Number(element, "width"), Number(element, "height")), Number(element, "rx"), Number(element, "rx"));
                else if (element.LocalName == "path") context.DrawGeometry(fill, stroke, Geometry.Parse(element.GetAttribute("d")));
                else throw new InvalidDataException("Unsupported logo geometry: " + element.LocalName);
            }
        }
        drawing.Freeze();
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = new List<byte[]>();
        foreach (int size in sizes) frames.Add(Render(drawing, size));
        Directory.CreateDirectory(args[1]);
        File.WriteAllBytes(Path.Combine(args[1], "logo.png"), frames[frames.Count - 1]);
        using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(args[1], "app.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
        return 0;
    }

    private static Brush BrushValue(string value)
    { return string.IsNullOrEmpty(value) || value == "none" ? null : (Brush)new BrushConverter().ConvertFromInvariantString(value); }
    private static double Number(XmlElement element, string name)
    { return double.Parse(element.GetAttribute(name), CultureInfo.InvariantCulture); }
    private static byte[] Render(Drawing drawing, int size)
    {
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size * 4.0 / 64, size * 4.0 / 64));
            context.DrawDrawing(drawing); context.Pop();
        }
        RenderTargetBitmap bitmap = new RenderTargetBitmap(size * 4, size * 4, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        TransformedBitmap reduced = new TransformedBitmap(bitmap, new ScaleTransform(0.25, 0.25));
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(reduced));
        using (MemoryStream stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
    }
}
