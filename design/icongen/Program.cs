using SkiaSharp;
using Svg.Skia;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: icongen <in.svg> <outDir>");
    return 1;
}

var input = args[0];
var outDir = args[1];
Directory.CreateDirectory(outDir);

using var svg = new SKSvg();
if (svg.Load(input) is null || svg.Picture is null)
{
    Console.Error.WriteLine("failed to load svg: " + input);
    return 2;
}

var pic = svg.Picture;
var rect = pic.CullRect;
float srcW = rect.Width > 0 ? rect.Width : 256f;
float srcH = rect.Height > 0 ? rect.Height : 256f;

byte[] RenderPng(int size)
{
    using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);
    var scale = size / Math.Max(srcW, srcH);
    canvas.Scale(scale);
    canvas.DrawPicture(pic);
    canvas.Flush();
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var pngs = sizes.ToDictionary(s => s, RenderPng);

foreach (var s in sizes)
    File.WriteAllBytes(Path.Combine(outDir, $"icon-{s}.png"), pngs[s]);
File.WriteAllBytes(Path.Combine(outDir, "icon.png"), pngs[256]); // Avalonia window icon

// Pack a PNG-in-ICO (Vista+). width/height byte 0 == 256.
using (var fs = File.Create(Path.Combine(outDir, "toolbax.ico")))
using (var w = new BinaryWriter(fs))
{
    w.Write((short)0);              // reserved
    w.Write((short)1);              // type: icon
    w.Write((short)sizes.Length);   // count
    int offset = 6 + 16 * sizes.Length;
    foreach (var s in sizes)
    {
        var data = pngs[s];
        w.Write((byte)(s >= 256 ? 0 : s)); // width
        w.Write((byte)(s >= 256 ? 0 : s)); // height
        w.Write((byte)0);                  // color count
        w.Write((byte)0);                  // reserved
        w.Write((short)1);                 // planes
        w.Write((short)32);                // bits per pixel
        w.Write(data.Length);              // bytes of image data
        w.Write(offset);                   // offset of image data
        offset += data.Length;
    }
    foreach (var s in sizes) w.Write(pngs[s]);
}

Console.WriteLine($"OK: wrote toolbax.ico + icon.png (+ per-size PNGs) to {outDir}");
return 0;
