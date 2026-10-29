using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AoeFontRepair
{
    public sealed class Inspection
    {
        public string Status, Details;
        public bool CanApply, CanRestore;
    }
    [DataContract] internal sealed class FileState
    {
        [DataMember] public string Name;
        [DataMember] public string OriginalSha256;
        [DataMember] public string InstalledSha256;
    }
    [DataContract] internal sealed class InstallState
    {
        [DataMember] public string Product = "AoEDE-Chinese-Font-Fix";
        [DataMember] public int FormatVersion = 1;
        [DataMember] public string ToolVersion = "0.1.0";
        [DataMember] public string CreatedUtc;
        [DataMember] public string UpdatedUtc;
        [DataMember] public string RegularFont;
        [DataMember] public string BoldFont;
        [DataMember] public string RegularFontSha256;
        [DataMember] public string BoldFontSha256;
        [DataMember] public List<FileState> Files = new List<FileState>();
    }
    [DataContract] internal sealed class TransactionFile
    {
        [DataMember] public string Name;
        [DataMember] public string BeforeSha256;
        [DataMember] public string AfterSha256;
    }
    [DataContract] internal sealed class Transaction
    {
        [DataMember] public int FormatVersion = 1;
        [DataMember] public string Stage;
        [DataMember] public string Operation;
        [DataMember] public InstallState Before;
        [DataMember] public InstallState After;
        [DataMember] public List<TransactionFile> Files = new List<TransactionFile>();
    }
    internal sealed class KnownFile
    {
        internal string Name, Dds, Fnt;
        internal KnownFile(string name, string dds, string fnt) { Name = name; Dds = dds; Fnt = fnt; }
    }

    /// <summary>
    /// Offline, version-pinned resource replacement. Only the 16 listed DDS files
    /// are replaced. Existing resources are never accepted merely by their size.
    /// A transaction is journalled before the first game-file replacement; after
    /// interruption, Restore first rolls it back and then restores pristine DDS.
    /// </summary>
    public static class PatchEngine
    {
        private const string BackupName = "AoEDE-Chinese-Font-Fix-backup";
        // SHA-256 digests only; no game assets are embedded or redistributed.
        private static readonly KnownFile[] Known = new KnownFile[]
        {
            new KnownFile("arial_13", "AC9D074924FECA1E0419C0FD6138CC85AA25AC625E6A90030ED94D7093D16ACB", "03BBCEFFD0D3BED62CE4E53427D9CB07FEDA7553D7BBE43AA9CF61A0894E7C5D"),
            new KnownFile("arial_15", "767E721B1435BBB98117A0C2A0D74CD0E401CCD6C09D85E9706842FE89011D7C", "0E0CB3AE429C08ECDD3549118995B0961DEFCE3D352D5E10935935BFB1A1264C"),
            new KnownFile("arial_22", "FC5D6B4C019A7EC4D90EC6E5866DD3A3AECDB670258ED1AA1BAA3D6B55EDB4E1", "A346865872CE2DFA7F9139EBF3535EB313FC04618A4EC1A8B2738BAC0773A8A8"),
            new KnownFile("arial_44", "B89A258BA1F4798630F841C1E594D99945C6BF04984DCD70A0C98F0DCB5B5DD2", "7ADE80CF5814751FB94F1F60583D36FFEABA8200E0CABED2B15B38F55AE48DD2"),
            new KnownFile("arial_bold_11", "512ED7BFD0DD059915B9E25CC2DE3A280264A2914ADF5C503BA78B79EABA0E88", "7ABB360C94EE9C283EC58889505FDA595A0BD574E978590ED1CA085F60C9B81C"),
            new KnownFile("arial_bold_13", "27CD1B47CEE9EA725A9D08E5E4F38F93A6BE3C5ADACE528783C371562234D588", "EEEFABA03679513AE69CD7BAC3A3CC738245664BADDAD5D920EE4B0117FF7B5B"),
            new KnownFile("arial_bold_15", "78181ABD8F8D38EF105475356CE936338FD099BF8F41FEEA51314F8A87516B23", "ED39FD6E37BE3E88278E9D63F239ABAFB30640D31AC88756BEA7E1C46D5C8D88"),
            new KnownFile("arial_bold_16", "05815FA6FBB77B742C698AC1D6D54ADA0B8AA133EBC03C78F354FA4D811F6CA9", "DDB4C72E9CC89B78019DDCDF1A6149A598B38D18D6BD74BFB7965DF9956D43B6"),
            new KnownFile("arial_bold_22", "77E3D5ACAE564AF328AA27AF9075E79F4D904EEC151A29053C8980B8A13AEF28", "66EE76A05FB492D82A545881294C0C495209BF2F6FA43ADB3C5BA5E924B18102"),
            new KnownFile("arial_bold_26", "EAEED819E57F131B37EFF1376F838631EAD1DBEE0EC06699408A1643282DAA8A", "87F48A68BF0F73C9359F8AC18F0FDCEB4EC668DFADC804B5EC29940DEDA9EF2D"),
            new KnownFile("arial_bold_40", "5C9B8E7F4605D3232911C911B3CBBF78946D0A0F1BF089FF74885D4A15EA6382", "996EA69120B8B4CEF190AA92CAE871E5D3C34214945918998D5E2D525EBA9EE6"),
            new KnownFile("arial_bold_44", "E84997A43F2600F5008C6991FD8805591D5D24CFD97EA9192E3F4DCAFE63E723", "471249C107D87A37A11A7B4D261D4AF0B7FA1758192ED2F88715B8A7904AA345"),
            new KnownFile("arial_bold_80", "D329D12300BF16A7E1D5148707B7A3EFC059EFA38515281C8C1C488E17672431", "D0BEE5D78639133FD9236164ADC4EA33ABB8A1D834FD988F3C7D7B350819A384"),
            new KnownFile("coprgtb_16", "38CAEC7FCD37279899F4012EE363E33D1AAB3834A21ECE496E96C1974D0817CD", "132AB1D42A7EB52AC22677767CB3FF863870CDA7E08DC88D3D7BE59F862FCC83"),
            new KnownFile("lbrited_27", "501099A692875A19ED3CF16170FA223560E5424E2A1C3B3340E8782FAE3AB7FE", "6B0B5D8706BB14E43BA6F64BF3EB06897E46208847E0F594635D508F26EECF5B"),
            new KnownFile("sugoeuib_21", "0A09A7027A4040698F54FC206526BF1824D1DF7BB84753D92378A679161BA670", "DAAAE3A202BC9CD965D5D900C22841C0237DF1C570C999206CF7CD6E3820EFB9"),
        };
        private sealed class Context
        {
            internal string Root, Fonts, Backup, Originals, StatePath, JournalPath;
            internal Context(string root)
            {
                if (String.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("请选择 AoEDE 游戏目录。");
                Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Root.StartsWith(@"\\", StringComparison.Ordinal) || Root.Length < 4)
                    throw new InvalidOperationException("请选择本机磁盘中的 AoEDE 游戏目录。");
                AssertSafe(Root);
                Fonts = Path.Combine(Root, "Assets", "Fonts");
                Backup = Path.Combine(Root, BackupName);
                Originals = Path.Combine(Backup, "original");
                StatePath = Path.Combine(Backup, "state.json");
                JournalPath = Path.Combine(Backup, "transaction.json");
                AssertSafe(Fonts); AssertSafe(Backup);
                if (!Directory.Exists(Fonts) || !File.Exists(Path.Combine(Root, "AoEDE_s.exe")))
                    throw new InvalidOperationException("目录中未找到 AoEDE_s.exe 和 Assets\\Fonts。请选择 Steam 版第一代决定版的游戏根目录。");
                AssertSafe(Path.Combine(Root, "AoEDE_s.exe"));
            }
            internal string Live(string name, string extension) { return Path.Combine(Fonts, name + extension); }
        }
        private sealed class OperationLock : IDisposable
        {
            private Mutex mutex;
            internal OperationLock(string root)
            {
                mutex = new Mutex(false, "Local\\AoEDEFontFix-" + Hash(Encoding.UTF8.GetBytes(root.ToUpperInvariant())).Substring(0, 24));
                bool owned = false;
                try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) { mutex.Dispose(); mutex = null; throw new InvalidOperationException("另一个字体修复操作正在处理这个游戏目录。"); }
            }
            public void Dispose() { if (mutex != null) { mutex.ReleaseMutex(); mutex.Dispose(); mutex = null; } }
        }
        private static void Log(Action<string> log, string value)
        {
            // A closed UI/log destination must not turn a successfully committed
            // filesystem transaction into a reported rollback failure.
            if (log != null) { try { log(value); } catch { } }
        }
        internal static string Hash(byte[] data) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
        internal static string HashFile(string path)
        {
            AssertSafe(path);
            using (SHA256 sha = SHA256.Create()) using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        // No path supplied by a journal is ever used directly. All game targets
        // come from Known; the only journal path component is a strict stage ID.
        private static void AssertSafe(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("为避免写入意外位置，暂不支持符号链接或目录联接：" + current);
                string parent = Path.GetDirectoryName(current);
                if (parent == current) break;
                current = parent;
            }
        }
        private static bool IsHash(string hash) { return hash != null && Regex.IsMatch(hash, "^[A-Fa-f0-9]{64}$"); }
        private static bool EqualHash(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static void RequireHash(string path, string hash)
        {
            if (!File.Exists(path) || !EqualHash(HashFile(path), hash)) throw new InvalidOperationException("文件与已验证版本不一致：" + Path.GetFileName(path) + "。可能是游戏更新、文件损坏或其他 mod；已停止覆盖。");
        }
        private static bool GameRunning(string root)
        {
            foreach (string name in new string[] { "AoEDE_s", "AoEDE" }) foreach (Process p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(p.MainModule.FileName);
                        if (String.Equals(Path.GetFullPath(folder).TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { return true; }
                }
            }
            return false;
        }
        private static void RequireClosed(Context c) { if (GameRunning(c.Root)) throw new InvalidOperationException("请先退出游戏，再安装或恢复字体。"); }
        private static T ReadJson<T>(string path)
        {
            AssertSafe(path);
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length > 1024 * 1024) throw new InvalidOperationException("备份记录缺失或大小异常。");
            using (FileStream stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static void SaveJson<T>(string path, T value)
        {
            AssertSafe(path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); stream.Flush(true); }
                AssertSafe(path);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static void ValidateState(InstallState state)
        {
            if (state == null || state.Product != "AoEDE-Chinese-Font-Fix" || state.FormatVersion != 1 || state.Files == null || state.Files.Count != Known.Length)
                throw new InvalidOperationException("备份记录格式不受支持。请保留备份目录，不要手动覆盖游戏文件。");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileState file in state.Files)
            {
                KnownFile known = Known.FirstOrDefault(k => k.Name == file.Name);
                if (known == null || !seen.Add(file.Name) || !EqualHash(file.OriginalSha256, known.Dds) || !IsHash(file.InstalledSha256))
                    throw new InvalidOperationException("备份记录内容无效。");
            }
        }
        private static InstallState LoadState(Context c)
        {
            if (!File.Exists(c.StatePath)) return null;
            InstallState state = ReadJson<InstallState>(c.StatePath); ValidateState(state); return state;
        }
        private static void ValidateOriginals(Context c)
        {
            ValidateOriginalsAt(c.Originals);
        }
        private static void ValidateOriginalsAt(string originals)
        {
            foreach (KnownFile known in Known)
            {
                RequireHash(Path.Combine(originals, known.Name + ".dds"), known.Dds);
                RequireHash(Path.Combine(originals, known.Name + ".fnt"), known.Fnt);
            }
        }
        private static void ValidateLive(Context c, InstallState state)
        {
            foreach (KnownFile known in Known)
            {
                RequireHash(c.Live(known.Name, ".fnt"), known.Fnt);
                string actual = HashFile(c.Live(known.Name, ".dds"));
                string installed = state == null ? known.Dds : state.Files.Single(f => f.Name == known.Name).InstalledSha256;
                if (!EqualHash(actual, known.Dds) && !EqualHash(actual, installed))
                    throw new InvalidOperationException("检测到未识别的字体文件：" + known.Name + ".dds。请先恢复其他字体修改，或用 Steam 验证游戏文件后重试。");
            }
        }
        private static Transaction LoadTransaction(Context c)
        {
            Transaction t = ReadJson<Transaction>(c.JournalPath);
            if (t == null || t.FormatVersion != 1 || t.Stage == null || !Regex.IsMatch(t.Stage, "^stage-[a-f0-9]{32}$") || (t.Operation != "apply" && t.Operation != "restore") || t.Files == null || t.Files.Count != Known.Length)
                throw new InvalidOperationException("事务恢复记录无效。请保留备份并停止手动覆盖。");
            ValidateState(t.Before); ValidateState(t.After);
            HashSet<string> seen = new HashSet<string>();
            foreach (TransactionFile file in t.Files)
            {
                if (!Known.Any(k => k.Name == file.Name) || !seen.Add(file.Name) || !IsHash(file.BeforeSha256) || !IsHash(file.AfterSha256))
                    throw new InvalidOperationException("事务文件清单无效。");
                if (!EqualHash(file.AfterSha256, t.After.Files.Single(f => f.Name == file.Name).InstalledSha256))
                    throw new InvalidOperationException("事务目标校验值不一致。");
                KnownFile known = Known.Single(k => k.Name == file.Name);
                string previous = t.Before.Files.Single(f => f.Name == file.Name).InstalledSha256;
                if (!EqualHash(file.BeforeSha256, previous) && !EqualHash(file.BeforeSha256, known.Dds))
                    throw new InvalidOperationException("事务起点校验值不一致。");
            }
            AssertSafe(Path.Combine(c.Backup, t.Stage));
            return t;
        }
        private static void ValidateRecoverable(Context c, Transaction t)
        {
            ValidateOriginals(c);
            foreach (KnownFile known in Known) RequireHash(c.Live(known.Name, ".fnt"), known.Fnt);
            foreach (TransactionFile f in t.Files)
            {
                RequireHash(Path.Combine(c.Backup, t.Stage, "before", f.Name + ".dds"), f.BeforeSha256);
                string current = HashFile(c.Live(f.Name, ".dds"));
                if (!EqualHash(current, f.BeforeSha256) && !EqualHash(current, f.AfterSha256))
                    throw new InvalidOperationException("中断后文件又被其他程序修改：" + f.Name + "。为保护新文件，未执行恢复。");
            }
        }
        public static Inspection Inspect(string gameDir)
        {
            try
            {
                Context c = new Context(gameDir);
                using (new OperationLock(c.Root))
                {
                    bool running = GameRunning(c.Root);
                    if (File.Exists(c.JournalPath))
                    {
                        Transaction t = LoadTransaction(c); ValidateRecoverable(c, t);
                        return new Inspection { Status = "检测到未完成操作", Details = "请退出游戏后点击恢复。工具将先回滚中断操作，再恢复游戏原字体。", CanApply = false, CanRestore = !running };
                    }
                    InstallState state = LoadState(c);
                    if (state == null && Directory.Exists(c.Backup)) throw new InvalidOperationException("备份目录存在，但没有完整的本工具记录。请保留该目录并检查；工具不会覆盖未知备份。");
                    ValidateLive(c, state); if (state != null) ValidateOriginals(c);
                    bool modified = Known.Any(k => !EqualHash(HashFile(c.Live(k.Name, ".dds")), k.Dds));
                    return new Inspection
                    {
                        Status = running ? "游戏正在运行" : modified ? "已安装本工具字体" : "已验证游戏原始字体",
                        Details = (running ? "请先退出游戏。\r\n" : "") + "资源校验通过：16 个字体贴图与对应布局文件。支持资源版本：Steam build 12673858 / 游戏 97381。" + (state == null ? "\r\n首次安装时会自动备份原始字体。" : "\r\n原始备份完整；恢复不会改动存档或场景。"),
                        CanApply = !running, CanRestore = !running && state != null && modified
                    };
                }
            }
            catch (Exception ex) { return new Inspection { Status = "暂不能操作", Details = ex.Message, CanApply = false, CanRestore = false }; }
        }
        private static InstallState MakeInitialState()
        {
            InstallState state = new InstallState { CreatedUtc = DateTime.UtcNow.ToString("o"), UpdatedUtc = DateTime.UtcNow.ToString("o") };
            foreach (KnownFile k in Known) state.Files.Add(new FileState { Name = k.Name, OriginalSha256 = k.Dds, InstalledSha256 = k.Dds });
            return state;
        }
        private static InstallState CloneState(InstallState state)
        {
            using (MemoryStream ms = new MemoryStream())
            { DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(InstallState)); ser.WriteObject(ms, state); ms.Position = 0; return (InstallState)ser.ReadObject(ms); }
        }
        private static InstallState EnsureBackup(Context c, Action<string> log)
        {
            InstallState state = LoadState(c);
            if (state != null) { ValidateOriginals(c); return state; }
            if (Directory.Exists(c.Backup)) throw new InvalidOperationException("已有未知或不完整备份目录，未覆盖。请检查 " + BackupName + "。");
            ValidateLive(c, null); RequireClosed(c);
            // Publish a complete first backup in one same-volume directory move.
            // An interruption here leaves no unusable official backup directory,
            // and no live game files have been modified yet.
            string staging = Path.Combine(c.Root, BackupName + "-initializing-" + Guid.NewGuid().ToString("N"));
            string stagingOriginals = Path.Combine(staging, "original");
            AssertSafe(staging); Directory.CreateDirectory(stagingOriginals);
            try
            {
                foreach (KnownFile k in Known)
                {
                    foreach (string ext in new string[] { ".dds", ".fnt" })
                    {
                        string source = c.Live(k.Name, ext), destination = Path.Combine(stagingOriginals, k.Name + ext);
                        RequireHash(source, ext == ".dds" ? k.Dds : k.Fnt);
                        File.Copy(source, destination, false);
                    }
                }
                ValidateOriginalsAt(stagingOriginals); ValidateLive(c, null); RequireClosed(c);
                state = MakeInitialState(); SaveJson(Path.Combine(staging, "state.json"), state);
                AssertSafe(c.Backup); AssertSafe(staging);
                if (Directory.Exists(c.Backup) || File.Exists(c.Backup)) throw new InvalidOperationException("备份目录在操作期间出现，已停止以避免覆盖。");
                Directory.Move(staging, c.Backup);
            }
            finally
            {
                // Only files created for this initialization are eligible for cleanup.
                if (Directory.Exists(staging))
                {
                    try
                    {
                        AssertSafe(stagingOriginals);
                        foreach (KnownFile k in Known) foreach (string ext in new string[] { ".dds", ".fnt" })
                        { string file = Path.Combine(stagingOriginals, k.Name + ext); AssertSafe(file); if (File.Exists(file)) File.Delete(file); }
                        if (Directory.Exists(stagingOriginals) && !Directory.EnumerateFileSystemEntries(stagingOriginals).Any()) Directory.Delete(stagingOriginals);
                        string record = Path.Combine(staging, "state.json"); AssertSafe(record); if (File.Exists(record)) File.Delete(record);
                        if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
                    }
                    catch (Exception ex) { Log(log, "初始备份临时文件清理提示：" + ex.Message); }
                }
            }
            Log(log, "原始资源备份已建立并校验。"); return state;
        }
        private static string NewStage(Context c)
        {
            string name = "stage-" + Guid.NewGuid().ToString("N"), dir = Path.Combine(c.Backup, name);
            AssertSafe(dir); Directory.CreateDirectory(dir); return name;
        }
        private static void ReplaceChecked(Context c, string source, string target, string expectedCurrent, string expectedSource)
        {
            RequireClosed(c); AssertSafe(target); AssertSafe(source);
            RequireHash(source, expectedSource); RequireHash(target, expectedCurrent);
            string temp = target + ".aoecjk-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                RequireHash(temp, expectedSource); RequireHash(target, expectedCurrent); RequireClosed(c); AssertSafe(target);
                File.Replace(temp, target, null);
                RequireHash(target, expectedSource);
            }
            finally { if (File.Exists(temp)) { AssertSafe(temp); File.Delete(temp); } }
        }
        private static void CleanStage(Context c, string stage)
        {
            // Cleanup is intentionally allowlisted, never a recursive delete.
            string root = Path.Combine(c.Backup, stage); AssertSafe(root);
            foreach (string folder in new string[] { "before", "dds" })
            {
                string dir = Path.Combine(root, folder); AssertSafe(dir);
                if (!Directory.Exists(dir)) continue;
                foreach (KnownFile k in Known) { string file = Path.Combine(dir, k.Name + ".dds"); AssertSafe(file); if (File.Exists(file)) File.Delete(file); }
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            string preview = Path.Combine(root, "preview.png"); AssertSafe(preview); if (File.Exists(preview)) File.Delete(preview);
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        private static void Recover(Context c, Action<string> log)
        {
            Transaction t = LoadTransaction(c); ValidateRecoverable(c, t); RequireClosed(c);
            Log(log, "正在回滚上次未完成的操作……");
            foreach (TransactionFile f in t.Files)
            {
                string target = c.Live(f.Name, ".dds"), current = HashFile(target);
                if (!EqualHash(current, f.BeforeSha256)) ReplaceChecked(c, Path.Combine(c.Backup, t.Stage, "before", f.Name + ".dds"), target, f.AfterSha256, f.BeforeSha256);
            }
            foreach (TransactionFile f in t.Files) RequireHash(c.Live(f.Name, ".dds"), f.BeforeSha256);
            SaveJson(c.StatePath, t.Before);
            AssertSafe(c.JournalPath); File.Delete(c.JournalPath);
            try { CleanStage(c, t.Stage); } catch (Exception ex) { Log(log, "已恢复；临时文件清理提示：" + ex.Message); }
        }
        private static void Commit(Context c, string stage, string operation, InstallState before, InstallState after, Action<string> log)
        {
            ValidateLive(c, before); ValidateOriginals(c); RequireClosed(c);
            string dir = Path.Combine(c.Backup, stage), beforeDir = Path.Combine(dir, "before");
            Directory.CreateDirectory(beforeDir); AssertSafe(beforeDir);
            Transaction t = new Transaction { Stage = stage, Operation = operation, Before = before, After = after };
            foreach (KnownFile k in Known)
            {
                string target = c.Live(k.Name, ".dds"), oldHash = HashFile(target), next = after.Files.Single(f => f.Name == k.Name).InstalledSha256;
                string nextPath = operation == "restore" ? Path.Combine(c.Originals, k.Name + ".dds") : Path.Combine(dir, "dds", k.Name + ".dds");
                RequireHash(nextPath, next);
                File.Copy(target, Path.Combine(beforeDir, k.Name + ".dds"), false);
                RequireHash(Path.Combine(beforeDir, k.Name + ".dds"), oldHash);
                t.Files.Add(new TransactionFile { Name = k.Name, BeforeSha256 = oldHash, AfterSha256 = next });
            }
            foreach (TransactionFile f in t.Files) RequireHash(c.Live(f.Name, ".dds"), f.BeforeSha256);
            RequireClosed(c); SaveJson(c.JournalPath, t);
            try
            {
                foreach (TransactionFile f in t.Files)
                {
                    if (EqualHash(f.BeforeSha256, f.AfterSha256)) continue;
                    string source = operation == "restore" ? Path.Combine(c.Originals, f.Name + ".dds") : Path.Combine(dir, "dds", f.Name + ".dds");
                    ReplaceChecked(c, source, c.Live(f.Name, ".dds"), f.BeforeSha256, f.AfterSha256);
                    Log(log, (operation == "restore" ? "已恢复 " : "已应用 ") + f.Name + ".dds");
                }
                foreach (TransactionFile f in t.Files) RequireHash(c.Live(f.Name, ".dds"), f.AfterSha256);
                foreach (KnownFile k in Known) RequireHash(c.Live(k.Name, ".fnt"), k.Fnt);
                SaveJson(c.StatePath, after); AssertSafe(c.JournalPath); File.Delete(c.JournalPath);
                Log(log, operation == "restore" ? "原始字体已恢复并校验。" : "全部字体修改已完成并校验。");
            }
            catch (Exception original)
            {
                try { Recover(c, log); }
                catch (Exception rollback) { throw new InvalidOperationException("操作未完成，恢复记录已保留。请关闭游戏后重新打开工具并点击恢复。原因：" + original.Message + "；回滚提示：" + rollback.Message, original); }
                throw new InvalidOperationException("操作失败，已自动回滚：" + original.Message, original);
            }
            try { CleanStage(c, stage); } catch (Exception ex) { Log(log, "修改已完成；临时文件清理提示：" + ex.Message); }
        }
        public static void Apply(string gameDir, string regularFontPath, string boldFontPath, Action<string> log)
        {
            Context c = new Context(gameDir);
            using (new OperationLock(c.Root))
            {
                RequireClosed(c);
                if (File.Exists(c.JournalPath)) throw new InvalidOperationException("上次操作未完成，请先点击恢复。");
                InstallState state = LoadState(c); ValidateLive(c, state);
                // Validate the font before creating a game backup.
                using (FontFace a = new FontFace(regularFontPath)) using (FontFace b = new FontFace(String.IsNullOrWhiteSpace(boldFontPath) ? regularFontPath : boldFontPath)) { Log(log, "所选字体：" + a.Name + " / " + b.Name); }
                state = EnsureBackup(c, log); string stage = NewStage(c), stagePath = Path.Combine(c.Backup, stage);
                string regularHash = HashFile(regularFontPath), boldFile = String.IsNullOrWhiteSpace(boldFontPath) ? regularFontPath : boldFontPath, boldHash = HashFile(boldFile);
                try
                {
                    Log(log, "正在本机生成全部汉字字形。此阶段尚未修改游戏字体……");
                    Report report = AtlasRenderer.Run(c.Originals, stagePath, regularFontPath, boldFile, false, delegate(string text) { Log(log, text); });
                    if (report.Files.Count != Known.Length) throw new InvalidOperationException("生成的贴图数量不正确。");
                    RequireHash(regularFontPath, regularHash); RequireHash(boldFile, boldHash);
                    InstallState next = CloneState(state); next.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    next.RegularFont = Path.GetFileName(regularFontPath); next.BoldFont = Path.GetFileName(boldFile);
                    next.RegularFontSha256 = regularHash; next.BoldFontSha256 = boldHash;
                    foreach (FileReport file in report.Files) next.Files.Single(f => f.Name + ".dds" == file.File).InstalledSha256 = file.CandidateSha256;
                    ValidateState(next); Commit(c, stage, "apply", state, next, log);
                }
                finally { if (!File.Exists(c.JournalPath)) { try { CleanStage(c, stage); } catch (Exception ex) { Log(log, "临时文件清理提示：" + ex.Message); } } }
            }
        }
        public static void Restore(string gameDir, Action<string> log)
        {
            Context c = new Context(gameDir);
            using (new OperationLock(c.Root))
            {
                RequireClosed(c);
                if (File.Exists(c.JournalPath)) Recover(c, log);
                InstallState state = LoadState(c);
                if (state == null) throw new InvalidOperationException("未找到本工具的备份记录。");
                ValidateOriginals(c); ValidateLive(c, state);
                InstallState next = CloneState(state); next.UpdatedUtc = DateTime.UtcNow.ToString("o");
                next.RegularFont = next.BoldFont = next.RegularFontSha256 = next.BoldFontSha256 = null;
                foreach (FileState f in next.Files) f.InstalledSha256 = f.OriginalSha256;
                string stage = NewStage(c);
                try { Commit(c, stage, "restore", state, next, log); }
                finally { if (!File.Exists(c.JournalPath)) { try { CleanStage(c, stage); } catch (Exception ex) { Log(log, "临时文件清理提示：" + ex.Message); } } }
            }
        }
        public static void GeneratePreview(string gameDir, string regularFontPath, string boldFontPath, string outputPng, Action<string> log)
        {
            Context c = new Context(gameDir);
            using (new OperationLock(c.Root))
            {
                if (File.Exists(c.JournalPath)) throw new InvalidOperationException("上次操作未完成，请先恢复。");
                InstallState state = LoadState(c); ValidateLive(c, state); if (state != null) ValidateOriginals(c);
                string output = Path.GetFullPath(outputPng); AssertSafe(output);
                if (!String.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("预览输出必须是 PNG 文件。");
                if (output.StartsWith(c.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请将预览保存在游戏目录之外。");
                string folder = Path.GetDirectoryName(output); Directory.CreateDirectory(folder);
                string temp = Path.Combine(folder, "aoecjk-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
                try
                {
                    Report report = AtlasRenderer.Run(state == null ? c.Fonts : c.Originals, temp, regularFontPath, boldFontPath, true, delegate(string text) { Log(log, text); });
                    AssertSafe(output); File.Copy(report.Preview, output, true);
                    Log(log, "预览已生成；游戏文件未修改。");
                }
                finally
                {
                    string preview = Path.Combine(temp, "preview.png"); AssertSafe(preview); if (File.Exists(preview)) File.Delete(preview);
                    if (!Directory.EnumerateFileSystemEntries(temp).Any()) Directory.Delete(temp);
                }
            }
        }
        public static string DetectGameDirectory()
        {
            List<string> steamRoots = new List<string>();
            try { string value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; if (!String.IsNullOrWhiteSpace(value)) steamRoots.Add(value.Replace('/', '\\')); } catch { }
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            List<string> libraries = new List<string>(steamRoots);
            foreach (string root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string config = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (!File.Exists(config)) continue;
                    foreach (Match m in Regex.Matches(File.ReadAllText(config), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"")) libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch { }
            }
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(library, "steamapps", "common", "AoEDE");
                if (File.Exists(Path.Combine(candidate, "AoEDE_s.exe"))) return candidate;
            }
            return "";
        }
    }
}
