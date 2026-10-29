using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AoeFontRepair
{
    // Each selected file is kept in its own private collection. No system-wide font
    // installation is performed. Coverage is checked against that file's cmap,
    // rather than accepting the operating system's per-character fallback fonts.
    internal sealed class FontFace : IDisposable
    {
        private readonly byte[] bytes;
        private readonly PrivateFontCollection collection = new PrivateFontCollection();
        private FontFamily family;
        private FontStyle style;
        private int cmap, format, glyphCount;
        internal string Name { get { return family.Name; } }
        internal FontFace(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !String.Equals(Path.GetExtension(path), ".ttf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择静态 TrueType .ttf 字体文件。");
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length < 64 || info.Length > 100 * 1024 * 1024)
                throw new InvalidOperationException("字体文件不存在或大小异常。");
            bytes = File.ReadAllBytes(path);
            if (Be32(0) != 0x00010000 && Be32(0) != 0x74727565)
                throw new InvalidOperationException("首版仅支持静态 TrueType TTF；不支持字体集合或 CFF 字体。");
            int count = Be16(4), cmapTable = -1, weight = 400;
            Check(12, count * 16);
            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(bytes, at, 4);
                long offset = Be32(at + 8), length = Be32(at + 12);
                if (offset + length > bytes.Length || offset > Int32.MaxValue) throw new InvalidOperationException("字体表越界。");
                if (tag == "fvar") throw new InvalidOperationException("暂不支持可变字体，请选择静态 TTF 字体。");
                if (tag == "cmap") cmapTable = (int)offset;
                if (tag == "maxp") glyphCount = Be16((int)offset + 4);
                if (tag == "OS/2") weight = Be16((int)offset + 4);
            }
            if (cmapTable < 0 || glyphCount == 0) throw new InvalidOperationException("字体缺少字符映射。");
            int subCount = Be16(cmapTable + 2), best = -1;
            Check(cmapTable + 4, subCount * 8);
            for (int i = 0; i < subCount; i++)
            {
                int at = cmapTable + 4 + i * 8;
                int platform = Be16(at), encoding = Be16(at + 2);
                if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10))) continue;
                long off = (long)cmapTable + Be32(at + 4);
                if (off > bytes.Length - 4) throw new InvalidOperationException("字体映射越界。");
                int f = Be16((int)off);
                if (f != 4 && f != 12) continue;
                int rank = f == 12 ? 2 : 1;
                if (rank > best) { best = rank; cmap = (int)off; format = f; }
            }
            if (best < 0) throw new InvalidOperationException("字体缺少受支持的 Unicode 映射。");
            if (format == 4) Check(cmap, Be16(cmap + 2));
            else { long len = Be32(cmap + 4); if (len > Int32.MaxValue) throw new InvalidOperationException("字体映射过大。"); Check(cmap, (int)len); }
            try
            {
                collection.AddFontFile(Path.GetFullPath(path));
                FontFamily[] families = collection.Families;
                if (families.Length != 1) throw new InvalidOperationException("字体文件应只包含一个字体族。");
                family = families[0];
                if (weight >= 600 && family.IsStyleAvailable(FontStyle.Bold)) style = FontStyle.Bold;
                else if (family.IsStyleAvailable(FontStyle.Regular)) style = FontStyle.Regular;
                else if (family.IsStyleAvailable(FontStyle.Bold)) style = FontStyle.Bold;
                else throw new InvalidOperationException("字体没有受支持的常规或粗体字重。");
                using (Font trial = Create(24)) { if (trial.Name != family.Name) throw new InvalidOperationException("字体被系统替代。"); }
            }
            catch { Dispose(); throw; }
        }
        private void Check(int at, int count) { if (at < 0 || count < 0 || (long)at + count > bytes.Length) throw new InvalidOperationException("字体数据越界。"); }
        private int Be16(int at) { Check(at, 2); return bytes[at] * 256 + bytes[at + 1]; }
        private uint Be32(int at) { Check(at, 4); return ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3]; }
        internal Font Create(float pixels) { return new Font(family, pixels, style, GraphicsUnit.Pixel); }
        internal void RequireGlyph(int cp)
        {
            long glyph = 0;
            if (format == 12)
            {
                uint count = Be32(cmap + 12);
                if (count > 65536) throw new InvalidOperationException("字体字符分组异常。");
                Check(cmap + 16, checked((int)count * 12));
                int lo = 0, hi = (int)count - 1;
                while (lo <= hi)
                {
                    int mid = lo + (hi - lo) / 2, at = cmap + 16 + mid * 12;
                    uint first = Be32(at), last = Be32(at + 4);
                    if (cp < first) hi = mid - 1;
                    else if (cp > last) lo = mid + 1;
                    else { glyph = (long)Be32(at + 8) + cp - first; break; }
                }
            }
            else
            {
                int segments = Be16(cmap + 6) / 2;
                Check(cmap + 14, segments * 8 + 2);
                for (int i = 0; i < segments; i++)
                {
                    int end = Be16(cmap + 14 + 2 * i), start = Be16(cmap + 16 + 2 * segments + 2 * i);
                    if (cp < start || cp > end) continue;
                    int delta = (short)Be16(cmap + 16 + 4 * segments + 2 * i);
                    int address = cmap + 16 + 6 * segments + 2 * i, range = Be16(address);
                    if (range == 0) glyph = (cp + delta) & 65535;
                    else { int g = Be16(address + range + 2 * (cp - start)); glyph = g == 0 ? 0 : (g + delta) & 65535; }
                    break;
                }
            }
            if (glyph <= 0 || glyph >= glyphCount) throw new InvalidOperationException("所选字体缺少汉字 U+" + cp.ToString("X4") + "（" + Char.ConvertFromUtf32(cp) + "）。未修改游戏文件。");
        }
        public void Dispose() { if (family != null) { family.Dispose(); family = null; } collection.Dispose(); }
    }

    public sealed class GlyphChange
    {
        public int CodePoint { get; set; }
        public string Character { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RenderPixels { get; set; }
        public bool SmallResizeRequired { get; set; }
        public int ChangedPixels { get; set; }
    }
    public sealed class FileReport
    {
        public string File { get; set; }
        public string OriginalSha256 { get; set; }
        public string CandidateSha256 { get; set; }
        public string FntSha256 { get; set; }
        public int NominalPixels { get; set; }
        public string Style { get; set; }
        public int TextureWidth { get; set; }
        public int TextureHeight { get; set; }
        public bool DdsHeaderUnchanged { get; set; }
        public bool OutsideTargetRectanglesUnchanged { get; set; }
        public bool FntUnchanged { get; set; }
        public bool TargetFontGlyphCoverageVerified { get; set; }
        public List<GlyphChange> Glyphs = new List<GlyphChange>();
    }
    public sealed class Report
    {
        public string SourceDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public string FontFamily { get; set; }
        public string Characters { get; set; }
        public bool AllCjk { get; set; }
        public string CjkRanges = "3400-4DBF;4E00-9FFF;F900-FAFF";
        public string Method = "Native pixel-size AntiAliasGridFit rendering; integer placement; original FNT/advance unchanged. Font may shrink by at most 15%; only remaining oversized axes are area-resampled into the original ink box.";
        public string Preview { get; set; }
        public List<FileReport> Files = new List<FileReport>();
        public List<string> Skipped = new List<string>();
    }
    internal sealed class Record
    {
        public int Code, Offset;
        public Rectangle Box;
        public bool Target;
    }
    internal sealed class Mask
    {
        public byte[] Pixels;
        public int Width, Height, FontPixels;
    }
    internal sealed class PreviewGlyph
    {
        public string Character;
        public Mask Old, New;
    }
    internal sealed class PreviewRow
    {
        public string Name, Style;
        public List<PreviewGlyph> Glyphs = new List<PreviewGlyph>();
    }

    internal static class AtlasRenderer
    {
        private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToUpperInvariant(); }
        private static uint U32(byte[] b, int offset) { return BitConverter.ToUInt32(b, offset); }
        private static float F32(byte[] b, int offset) { return BitConverter.ToSingle(b, offset); }
        private static bool IsCjk(int c) { return (c >= 0x3400 && c <= 0x4dbf) || (c >= 0x4e00 && c <= 0x9fff) || (c >= 0xf900 && c <= 0xfaff); }

        private static byte[] BitmapAlpha(Bitmap bitmap)
        {
            BitmapData d = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (d.Stride < 0) throw new InvalidOperationException("Negative bitmap stride");
                byte[] rgba = new byte[d.Stride * bitmap.Height];
                Marshal.Copy(d.Scan0, rgba, 0, rgba.Length);
                byte[] alpha = new byte[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++) alpha[y * bitmap.Width + x] = rgba[y * d.Stride + x * 4 + 3];
                return alpha;
            }
            finally { bitmap.UnlockBits(d); }
        }

        private static Rectangle Ink(byte[] alpha, int width, int height)
        {
            int x0 = width, y0 = height, x1 = -1, y1 = -1;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (alpha[y * width + x] != 0)
                { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < 0) throw new InvalidOperationException("No visible ink");
            return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        private sealed class Renderer : IDisposable
        {
            private readonly FontFace regular, bold;
            private readonly Dictionary<string, Mask> cache = new Dictionary<string, Mask>();
            private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
            public Renderer(FontFace regular, FontFace bold) { this.regular = regular; this.bold = bold; }
            public Mask Get(int cp, int pixels, FontStyle style)
            {
                string key = cp + ":" + pixels + ":" + (int)style;
                Mask hit; if (cache.TryGetValue(key, out hit)) return hit;
                int side = pixels * 2 + 32;
                string fontKey = pixels + ":" + (int)style;
                Font f;
                if (!fonts.TryGetValue(fontKey, out f))
                { f = (style == FontStyle.Bold ? bold : regular).Create(pixels); fonts.Add(fontKey, f); }
                using (Bitmap bitmap = new Bitmap(side, side, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(Char.ConvertFromUtf32(cp), f, Brushes.White, 8f, 8f, StringFormat.GenericTypographic);
                    byte[] alpha = BitmapAlpha(bitmap);
                    Rectangle r = Ink(alpha, side, side);
                    Mask mask = new Mask { Width = r.Width, Height = r.Height, Pixels = new byte[r.Width * r.Height], FontPixels = pixels };
                    for (int y = 0; y < r.Height; y++) Buffer.BlockCopy(alpha, (r.Y + y) * side + r.X, mask.Pixels, y * r.Width, r.Width);
                    cache.Add(key, mask);
                    return mask;
                }
            }
            public void Dispose() { cache.Clear(); foreach (Font f in fonts.Values) f.Dispose(); fonts.Clear(); }
        }

        // Area-average only when an original box is too narrow/short at all allowed native sizes.
        // This avoids shrinking simple wide glyphs such as 一 to half size just to reduce stroke height.
        private static Mask ResizeOversizedAxes(Mask source, int maxWidth, int maxHeight)
        {
            int w = Math.Min(source.Width, maxWidth), h = Math.Min(source.Height, maxHeight);
            if (w == source.Width && h == source.Height) return source;
            byte[] pixels = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double sx0 = x * source.Width / (double)w, sx1 = (x + 1) * source.Width / (double)w;
                double sy0 = y * source.Height / (double)h, sy1 = (y + 1) * source.Height / (double)h;
                double sum = 0;
                for (int sy = (int)Math.Floor(sy0); sy < Math.Ceiling(sy1); sy++)
                    for (int sx = (int)Math.Floor(sx0); sx < Math.Ceiling(sx1); sx++)
                    {
                        double overlap = (Math.Min(sx1, sx + 1) - Math.Max(sx0, sx)) * (Math.Min(sy1, sy + 1) - Math.Max(sy0, sy));
                        sum += source.Pixels[sy * source.Width + sx] * overlap;
                    }
                pixels[y * w + x] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(sum / ((sx1 - sx0) * (sy1 - sy0)))));
            }
            return new Mask { Width = w, Height = h, Pixels = pixels, FontPixels = source.FontPixels };
        }

        private static Mask NativeFit(Renderer renderer, int cp, int nominal, FontStyle style, Rectangle oldInk, int width, int height, out bool resized)
        {
            Mask glyph = null;
            int minPixels = Math.Max(4, (int)Math.Ceiling(nominal * .85));
            for (int pixels = nominal; pixels >= minPixels; pixels--)
            {
                glyph = renderer.Get(cp, pixels, style);
                if (glyph.Width <= oldInk.Width && glyph.Height <= oldInk.Height) break;
            }
            resized = glyph.Width > oldInk.Width || glyph.Height > oldInk.Height;
            glyph = ResizeOversizedAxes(glyph, oldInk.Width, oldInk.Height);
            int dx = oldInk.X + (oldInk.Width - glyph.Width) / 2, dy = oldInk.Y + (oldInk.Height - glyph.Height) / 2;
            Mask placed = new Mask { Width = width, Height = height, Pixels = new byte[width * height], FontPixels = glyph.FontPixels };
            if (dx < 0 || dy < 0 || dx + glyph.Width > width || dy + glyph.Height > height) throw new InvalidOperationException("Placement exceeds original rectangle");
            for (int y = 0; y < glyph.Height; y++) Buffer.BlockCopy(glyph.Pixels, y * glyph.Width, placed.Pixels, (dy + y) * width + dx, glyph.Width);
            return placed;
        }

        private static Bitmap MaskBitmap(Mask mask)
        {
            Bitmap bitmap = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);
            Rectangle all = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData d = bitmap.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] rgba = new byte[d.Stride * mask.Height];
            for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++)
            {
                byte value = (byte)(255 - mask.Pixels[y * mask.Width + x]);
                int index = y * d.Stride + x * 4;
                rgba[index] = rgba[index + 1] = rgba[index + 2] = value; rgba[index + 3] = 255;
            }
            Marshal.Copy(rgba, 0, d.Scan0, rgba.Length); bitmap.UnlockBits(d); return bitmap;
        }

        private static void Preview(List<PreviewRow> rows, string output)
        {
            int rowHeight = 128, width = 1420, height = 70 + rows.Count * rowHeight;
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font label = new Font("Microsoft YaHei UI", 18, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(Color.White); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString("每字：左为游戏原字形，右为所选字体；像素放大预览，实际布局不变", label, Brushes.Black, 20, 15);
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 0; i < rows.Count; i++)
                {
                    int top = 65 + i * rowHeight;
                    int rowMaxDimension = 1;
                    foreach (PreviewGlyph glyph in rows[i].Glyphs) rowMaxDimension = Math.Max(rowMaxDimension, Math.Max(glyph.Old.Width, glyph.Old.Height));
                    int rowScale = Math.Max(1, 82 / rowMaxDimension);
                    g.DrawString(rows[i].Name + "\n" + rows[i].Style, label, Brushes.Black, 15, top + 32);
                    for (int j = 0; j < rows[i].Glyphs.Count; j++)
                    {
                        PreviewGlyph pair = rows[i].Glyphs[j]; int left = 220 + j * 235;
                        g.DrawString(pair.Character, label, Brushes.DimGray, left + 83, top);
                        Mask[] masks = new Mask[] { pair.Old, pair.New };
                        for (int k = 0; k < 2; k++) using (Bitmap image = MaskBitmap(masks[k]))
                        {
                            int scale = rowScale;
                            int dw = image.Width * scale, dh = image.Height * scale;
                            g.DrawImage(image, new Rectangle(left + k * 108 + (100 - dw) / 2, top + 30 + (86 - dh) / 2, dw, dh), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
                        }
                    }
                    g.DrawLine(Pens.LightGray, 15, top + rowHeight - 5, width - 15, top + rowHeight - 5);
                }
                bitmap.Save(output, ImageFormat.Png);
            }
        }

        internal static Report Run(string sourceDirectory, string outputDirectory, string regularPath, string boldPath, bool previewOnly, Action<string> log)
        {
            string output = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(output);
            if (!previewOnly) Directory.CreateDirectory(Path.Combine(output, "dds"));
            const string characters = "置房腊腓比";
            bool allCjk = !previewOnly;
            HashSet<int> selected = new HashSet<int>(); foreach (char c in characters) selected.Add(c);
            Report report = new Report { SourceDirectory = sourceDirectory, OutputDirectory = output, Characters = characters, AllCjk = allCjk, FontFamily = Path.GetFileName(regularPath) };
            List<PreviewRow> previewRows = new List<PreviewRow>();
            string[] paths = Directory.GetFiles(sourceDirectory, "*.fnt"); Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            using (FontFace regular = new FontFace(regularPath))
            using (FontFace bold = new FontFace(String.IsNullOrWhiteSpace(boldPath) ? regularPath : boldPath))
            using (Renderer renderer = new Renderer(regular, bold)) foreach (string path in paths)
            {
                if (previewOnly && !new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "arial_15", "arial_22", "arial_bold_26", "arial_bold_44" }.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                byte[] fnt = File.ReadAllBytes(path); string fntHash = Hash(fnt);
                if (fnt.Length < 44) throw new InvalidOperationException("Invalid FNT header");
                if (U32(fnt, 0) != 0x00544e46) throw new InvalidOperationException("FNT magic");
                int count = (int)U32(fnt, 24), ranges = (int)U32(fnt, 28), recordStart = 44 + (ranges - 1) * 12;
                if (ranges < 1 || count < 1 || count > 65536 || recordStart < 44 || (long)recordStart + (long)count * 40 > fnt.Length) throw new InvalidOperationException("FNT bounds");
                string name = Path.GetFileNameWithoutExtension(path), ddsPath = Path.ChangeExtension(path, ".dds");
                byte[] original = File.ReadAllBytes(ddsPath);
                if (original.Length < 128) throw new InvalidOperationException("Invalid DDS header");
                int tw = (int)U32(original, 16), th = (int)U32(original, 12);
                if (Encoding.ASCII.GetString(original, 0, 4) != "DDS " || U32(original, 80) != 2 || U32(original, 88) != 8 || U32(original, 104) != 255 || tw <= 0 || th <= 0 || tw > 16384 || th > 16384 || original.Length != 128L + (long)tw * th) throw new InvalidOperationException("DDS is not A8: " + name);
                List<Record> records = new List<Record>(); List<Record> targets = new List<Record>();
                for (int i = 0; i < count; i++)
                {
                    int o = recordStart + i * 40, cp = (int)U32(fnt, o);
                    int x0 = (int)Math.Round(F32(fnt, o + 24) * tw), y0 = (int)Math.Round(F32(fnt, o + 28) * th);
                    int x1 = (int)Math.Round(F32(fnt, o + 32) * tw), y1 = (int)Math.Round(F32(fnt, o + 36) * th);
                    if (x1 <= x0 || y1 <= y0) continue;
                    if (x0 < 0 || y0 < 0 || x1 > tw || y1 > th) throw new InvalidOperationException("Glyph rectangle exceeds texture");
                    Record record = new Record { Code = cp, Offset = o, Box = Rectangle.FromLTRB(x0, y0, x1, y1), Target = allCjk ? IsCjk(cp) : selected.Contains(cp) };
                    records.Add(record); if (record.Target) targets.Add(record);
                }
                if (targets.Count == 0) { report.Skipped.Add(name + ": no selected glyphs"); continue; }
                records.Sort((a, b) => a.Box.Left.CompareTo(b.Box.Left));
                for (int i = 0; i < records.Count; i++) for (int j = i + 1; j < records.Count && records[j].Box.Left < records[i].Box.Right; j++)
                    if ((records[i].Target || records[j].Target) && records[i].Box.IntersectsWith(records[j].Box)) throw new InvalidOperationException("Target glyph overlap: " + name + " " + records[i].Code + "/" + records[j].Code);
                int nominal = (int)U32(fnt, 8);
                if (nominal < 4 || nominal > 200) throw new InvalidOperationException("Unexpected nominal size");
                FontStyle style = name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0 || name == "coprgtb_16" || name == "lbrited_27" || name == "sugoeuib_21" ? FontStyle.Bold : FontStyle.Regular;
                foreach (Record target in targets) (style == FontStyle.Bold ? bold : regular).RequireGlyph(target.Code);
                FileReport file = new FileReport { File = name + ".dds", OriginalSha256 = Hash(original), FntSha256 = fntHash, TextureWidth = tw, TextureHeight = th, NominalPixels = nominal, Style = style.ToString() };
                file.TargetFontGlyphCoverageVerified = true;
                PreviewRow previewRow = new PreviewRow { Name = name, Style = style.ToString() };
                byte[] patched = (byte[])original.Clone();
                foreach (Record record in targets)
                {
                    Rectangle box = record.Box; byte[] old = new byte[box.Width * box.Height];
                    for (int y = 0; y < box.Height; y++) Buffer.BlockCopy(original, 128 + (box.Y + y) * tw + box.X, old, y * box.Width, box.Width);
                    Rectangle ink = Ink(old, box.Width, box.Height);
                    bool resized; Mask replacement = NativeFit(renderer, record.Code, nominal, style, ink, box.Width, box.Height, out resized);
                    int differences = 0;
                    for (int y = 0; y < box.Height; y++) for (int x = 0; x < box.Width; x++)
                    {
                        int dst = 128 + (box.Y + y) * tw + box.X + x; byte value = replacement.Pixels[y * box.Width + x];
                        if (patched[dst] != value) differences++; patched[dst] = value;
                    }
                    file.Glyphs.Add(new GlyphChange { CodePoint = record.Code, Character = Char.ConvertFromUtf32(record.Code), X = box.X, Y = box.Y, Width = box.Width, Height = box.Height, RenderPixels = replacement.FontPixels, SmallResizeRequired = resized, ChangedPixels = differences });
                    if (selected.Contains(record.Code)) previewRow.Glyphs.Add(new PreviewGlyph { Character = Char.ConvertFromUtf32(record.Code), Old = new Mask { Pixels = old, Width = box.Width, Height = box.Height }, New = replacement });
                }
                previewRow.Glyphs.Sort((a,b) => characters.IndexOf(a.Character, StringComparison.Ordinal).CompareTo(characters.IndexOf(b.Character, StringComparison.Ordinal)));
                byte[] restored = (byte[])patched.Clone();
                foreach (Record record in targets) for (int y = 0; y < record.Box.Height; y++)
                { int index = 128 + (record.Box.Y + y) * tw + record.Box.X; Buffer.BlockCopy(original, index, restored, index, record.Box.Width); }
                if (Hash(restored) != file.OriginalSha256) throw new InvalidOperationException("Changes outside allowed rectangles");
                if (Hash(File.ReadAllBytes(path)) != fntHash) throw new InvalidOperationException("Source FNT changed during generation");
                file.CandidateSha256 = Hash(patched); file.DdsHeaderUnchanged = file.OutsideTargetRectanglesUnchanged = file.FntUnchanged = true;
                if (!previewOnly) File.WriteAllBytes(Path.Combine(output, "dds", name + ".dds"), patched);
                report.Files.Add(file); previewRows.Add(previewRow);
                if (log != null) log(name + "：" + targets.Count + " 个汉字，" + style);
            }
            report.Preview = Path.Combine(output, "preview.png"); Preview(previewRows, report.Preview);
            return report;
        }
    }
}
