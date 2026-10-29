using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AoeFontRepair;

// Integration fixture: pass an owned scratch directory, a legal local copy of
// the supported original Assets/Fonts directory, and the two test font paths.
// Never point the scratch directory at a real game installation.
internal static class CoreTests
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); passed++; }
    private static string Hash(string path) { using (SHA256 sha = SHA256.Create()) using (Stream s = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(s)); }
    private static Dictionary<string, string> Snapshot(string folder) { return Directory.GetFiles(folder).ToDictionary(Path.GetFileName, Hash); }
    private static bool Equal(Dictionary<string,string> snapshot, string folder) { return snapshot.All(kv => File.Exists(Path.Combine(folder, kv.Key)) && Hash(Path.Combine(folder, kv.Key)) == kv.Value); }
    private static void MustReject(Action action, string message) { bool threw = false; try { action(); } catch (Exception e) { threw = true; Console.WriteLine("Expected: " + e.Message); } Check(threw, message); }
    private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    private static void RequireFixture(string root)
    {
        string marker = Path.Combine(root, "AoEDE_s.exe");
        if (!File.Exists(marker) || new FileInfo(marker).Length > 100 || File.ReadAllText(marker) != "test fixture marker; not executable")
            throw new Exception("This test mode requires a fixture marker, never a real game installation.");
    }
    private static void TestStatePathValidation(string root)
    {
        RequireFixture(root); string fonts = Path.Combine(root, "Assets", "Fonts");
        Dictionary<string,string> snapshot = Snapshot(fonts);
        string record = Path.Combine(root, "AoEDE-Chinese-Font-Fix-backup", "state.json"); string stateJson = File.ReadAllText(record);
        try
        {
            File.WriteAllText(record, stateJson.Replace("\"arial_13\"", "\"../arial_13\""));
            MustReject(delegate { PatchEngine.Restore(root, null); }, "path traversal in state is rejected");
            Check(Equal(snapshot, fonts), "invalid-state recovery changes zero game files");
        }
        finally { File.WriteAllText(record, stateJson); }
    }
    private static void AuditOnlyHanPixels(string originals, string installed)
    {
        foreach (string path in Directory.GetFiles(installed, "*.dds"))
        {
            byte[] before = File.ReadAllBytes(Path.Combine(originals, Path.GetFileName(path))), after = File.ReadAllBytes(path);
            Check(before.Length == after.Length, Path.GetFileName(path) + " keeps file length");
            byte[] fnt = File.ReadAllBytes(Path.ChangeExtension(path, ".fnt"));
            int width = BitConverter.ToInt32(before, 16), height = BitConverter.ToInt32(before, 12);
            int count = BitConverter.ToInt32(fnt, 24), start = 44 + (BitConverter.ToInt32(fnt, 28) - 1) * 12;
            bool[] allowed = new bool[before.Length];
            for (int i = 0; i < count; i++)
            {
                int at = start + i * 40, cp = BitConverter.ToInt32(fnt, at);
                if (!((cp >= 0x3400 && cp <= 0x4dbf) || (cp >= 0x4e00 && cp <= 0x9fff) || (cp >= 0xf900 && cp <= 0xfaff))) continue;
                int left = (int)Math.Round(BitConverter.ToSingle(fnt, at + 24) * width), top = (int)Math.Round(BitConverter.ToSingle(fnt, at + 28) * height);
                int right = (int)Math.Round(BitConverter.ToSingle(fnt, at + 32) * width), bottom = (int)Math.Round(BitConverter.ToSingle(fnt, at + 36) * height);
                for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) allowed[128 + y * width + x] = true;
            }
            bool valid = true;
            for (int i = 0; i < after.Length; i++) if (!allowed[i] && after[i] != before[i]) { valid = false; break; }
            Check(valid, Path.GetFileName(path) + " preserves header and all non-Han pixels");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--crash-apply")
            {
                RequireFixture(args[1]);
                int writes = 0;
                PatchEngine.Apply(args[1], args[2], args[3], delegate(string line) { Console.WriteLine(line); if (line.StartsWith("已应用 ") && ++writes == 3) Environment.Exit(77); });
                return 2;
            }
            if (args.Length == 2 && args[0] == "--record-tests") { TestStatePathValidation(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL PASS " + passed); return 0; }
            if (args.Length < 4) { Console.Error.WriteLine("Usage: CoreTests.exe scratch-new-directory original-font-directory regular.ttf bold.ttf [--quick]"); return 2; }
            string root = Path.GetFullPath(args[0]), originals = Path.GetFullPath(args[1]), regular = Path.GetFullPath(args[2]), bold = Path.GetFullPath(args[3]);
            if (Directory.Exists(root)) throw new Exception("Scratch directory must not already exist.");
            if (root.Length < 10 || root.StartsWith(originals, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe scratch root.");
            string fonts = Path.Combine(root, "Assets", "Fonts"); Directory.CreateDirectory(fonts);
            foreach (string source in Directory.GetFiles(originals)) if (Path.GetExtension(source) == ".dds" || Path.GetExtension(source) == ".fnt") File.Copy(source, Path.Combine(fonts, Path.GetFileName(source)));
            File.WriteAllText(Path.Combine(root, "AoEDE_s.exe"), "test fixture marker; not executable");
            Dictionary<string,string> original = Snapshot(fonts);
            Inspection inspection = PatchEngine.Inspect(root); Console.WriteLine(inspection.Details);
            Check(inspection.CanApply && !inspection.CanRestore, "supported pristine resource recognition");
            string preview = Path.Combine(Path.GetDirectoryName(root), Path.GetFileName(root) + "-preview.png");
            PatchEngine.GeneratePreview(root, regular, bold, preview, Console.WriteLine);
            Check(File.Exists(preview) && new FileInfo(preview).Length > 1000, "preview created from selected file fonts");
            Check(Equal(original, fonts), "preview does not change any game resource");
            MustReject(delegate { PatchEngine.GeneratePreview(root, regular, bold, Path.Combine(fonts, "preview.png"), null); }, "preview cannot write inside game directory");
            string sample = Path.Combine(fonts, "arial_13.dds"); byte[] sampleBytes = File.ReadAllBytes(sample); byte[] changed = (byte[])sampleBytes.Clone(); changed[128] ^= 1; File.WriteAllBytes(sample, changed);
            Check(!PatchEngine.Inspect(root).CanApply, "unknown DDS rejected");
            MustReject(delegate { PatchEngine.Apply(root, regular, bold, null); }, "apply refuses unknown DDS before modifying other resources");
            File.WriteAllBytes(sample, sampleBytes); Check(Equal(original, fonts), "unknown-resource rejection leaves others unchanged");
            string fntPath = Path.Combine(fonts, "arial_13.fnt"); byte[] fntBytes = File.ReadAllBytes(fntPath); changed = (byte[])fntBytes.Clone(); changed[40] ^= 1; File.WriteAllBytes(fntPath, changed);
            Check(!PatchEngine.Inspect(root).CanApply, "changed FNT layout rejected"); File.WriteAllBytes(fntPath, fntBytes);
            string missing = Path.Combine(root, "missing-font.ttf");
            MustReject(delegate { PatchEngine.Apply(root, missing, null, null); }, "missing font rejected");
            Check(Equal(original, fonts), "missing-font failure leaves resources unchanged");
            string latinFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (File.Exists(latinFont))
            {
                MustReject(delegate { PatchEngine.GeneratePreview(root, latinFont, null, preview, null); }, "font without Han coverage is rejected instead of falling back");
                Check(Equal(original, fonts), "missing-glyph failure leaves resources unchanged");
            }
            string incomplete = Path.Combine(root, "AoEDE-Chinese-Font-Fix-backup-initializing-00000000000000000000000000000000");
            Directory.CreateDirectory(incomplete); File.WriteAllText(Path.Combine(incomplete, "keep.txt"), "simulated abandoned initialization");
            if (args.Length > 4 && args[4] == "--quick") { Console.WriteLine("TOTAL PASS " + passed); return 0; }
            PatchEngine.Apply(root, regular, bold, Console.WriteLine);
            Check(File.Exists(Path.Combine(incomplete, "keep.txt")), "abandoned initialization does not block retry and is not deleted");
            Dictionary<string,string> applied = Snapshot(fonts);
            int differences = applied.Count(kv => original[kv.Key] != kv.Value);
            Check(differences == 16 && applied.Where(kv => original[kv.Key] != kv.Value).All(kv => kv.Key.EndsWith(".dds")), "exactly 16 DDS changed; FNT and other files unchanged");
            AuditOnlyHanPixels(originals, fonts);
            Check(PatchEngine.Inspect(root).CanApply && PatchEngine.Inspect(root).CanRestore, "installed state recognized and reversible");
            byte[] installedSample = File.ReadAllBytes(sample); changed = (byte[])installedSample.Clone(); changed[129] ^= 1; File.WriteAllBytes(sample, changed);
            Dictionary<string,string> conflictSnapshot = Snapshot(fonts);
            MustReject(delegate { PatchEngine.Restore(root, null); }, "restore rejects unknown later modification");
            Check(Equal(conflictSnapshot, fonts), "conflict restore changes zero files"); File.WriteAllBytes(sample, installedSample);
            string savedOriginal = Path.Combine(root, "AoEDE-Chinese-Font-Fix-backup", "original", "arial_13.dds");
            File.WriteAllBytes(savedOriginal, changed);
            MustReject(delegate { PatchEngine.Restore(root, null); }, "restore rejects corrupted original backup");
            Check(Equal(applied, fonts), "corrupt-backup rejection changes zero game files"); File.WriteAllBytes(savedOriginal, sampleBytes);
            PatchEngine.Restore(root, Console.WriteLine);
            Check(Equal(original, fonts), "restoration returns all resources to exact original hashes");
            Check(PatchEngine.Inspect(root).CanApply && !PatchEngine.Inspect(root).CanRestore, "restored state recognized");
            TestStatePathValidation(root);
            ProcessStartInfo psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--crash-apply " + Quote(root) + " " + Quote(regular) + " " + Quote(bold));
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            using (Process child = Process.Start(psi)) { child.WaitForExit(); Check(child.ExitCode == 77, "simulated process interruption after three file replacements"); }
            Check(File.Exists(Path.Combine(root, "AoEDE-Chinese-Font-Fix-backup", "transaction.json")), "interrupted transaction retains recovery journal");
            inspection = PatchEngine.Inspect(root); Check(!inspection.CanApply && inspection.CanRestore, "interrupted transaction requires recovery");
            PatchEngine.Restore(root, Console.WriteLine);
            Check(Equal(original, fonts), "crash recovery restores exact original hashes");
            Check(!File.Exists(Path.Combine(root, "AoEDE-Chinese-Font-Fix-backup", "transaction.json")), "successful recovery clears journal");
            Console.WriteLine("TOTAL PASS " + passed); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
