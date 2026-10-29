using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("AoEDE Chinese Font Fix")]
[assembly: AssemblyDescription("Offline Chinese glyph repair for Age of Empires: Definitive Edition")]
[assembly: AssemblyCompany("AoEDE Chinese Font Fix contributors")]
[assembly: AssemblyProduct("AoEDE Chinese Font Fix")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyCopyright("Copyright (c) 2026 AoEDE Chinese Font Fix contributors")]

namespace AoeFontRepair
{
    internal static class Program
    {
        internal const string Version = "0.1.0";
        internal const string ProductName = "AoEDE 中文字体修复";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0) return RunCommandLine(args);
            try
            {
                // Use Windows' consistent bitmap scaling for this fixed 96-DPI layout.
                // Opting into DPI awareness without scaling all coordinates clips controls.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show("程序无法启动。\r\n\r\n" + e.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        internal static string BundledFont(string weight)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", "AoeCjkSans-" + weight + ".ttf");
        }

        private static int RunCommandLine(string[] args)
        {
            StreamWriter writer = null;
            try
            {
                AttachConsole(0xffffffff);
                Console.OutputEncoding = new UTF8Encoding(false);
                string action = null;
                Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i].TrimStart('-', '/').ToLowerInvariant();
                    if (key == "cli") continue;
                    if (key == "help" || key == "?" || key == "version")
                    {
                        Console.WriteLine("AoEDE Chinese Font Fix " + Version);
                        if (key != "version") Console.WriteLine(Usage());
                        return 0;
                    }
                    if (key == "inspect" || key == "preview" || key == "apply" || key == "restore")
                    {
                        if (action != null) throw new ArgumentException("一次只能指定一个操作。");
                        action = key;
                        continue;
                    }
                    if (key != "game" && key != "regular" && key != "bold" && key != "out" && key != "log")
                        throw new ArgumentException("无法识别的参数：" + args[i]);
                    if (++i >= args.Length) throw new ArgumentException("参数缺少值：" + key);
                    if (options.ContainsKey(key)) throw new ArgumentException("参数重复：" + key);
                    options.Add(key, args[i]);
                }
                if (action == null) throw new ArgumentException("请指定 --inspect、--preview、--apply 或 --restore。");
                string game = options.ContainsKey("game") ? options["game"] : PatchEngine.DetectGameDirectory();
                if (String.IsNullOrWhiteSpace(game)) throw new ArgumentException("未找到游戏，请使用 --game 指定 AoEDE 游戏根目录。");
                game = Path.GetFullPath(game);
                string regular = Path.GetFullPath(options.ContainsKey("regular") ? options["regular"] : BundledFont("Regular"));
                string bold = options.ContainsKey("bold") ? options["bold"] : (options.ContainsKey("regular") ? "" : BundledFont("Bold"));
                if (!String.IsNullOrWhiteSpace(bold)) bold = Path.GetFullPath(bold);
                string previewOutput = options.ContainsKey("out") ? Path.GetFullPath(options["out"]) : null;
                if (options.ContainsKey("log"))
                {
                    string logPath = Path.GetFullPath(options["log"]);
                    ValidateLogPath(logPath, game, regular, bold, previewOutput);
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    RejectReparseAncestors(logPath);
                    writer = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                    writer.AutoFlush = true;
                }
                Action<string> log = delegate(string text)
                {
                    Console.WriteLine(text);
                    if (writer != null) writer.WriteLine(text);
                };
                log("AoEDE Chinese Font Fix " + Version + " / " + action);
                if (action == "inspect")
                {
                    Inspection inspection = PatchEngine.Inspect(game);
                    log(inspection.Status);
                    log(inspection.Details);
                    log("CanApply=" + inspection.CanApply + "; CanRestore=" + inspection.CanRestore);
                    return inspection.CanApply || inspection.CanRestore ? 0 : 3;
                }
                if (action == "preview")
                {
                    if (!options.ContainsKey("out")) throw new ArgumentException("预览需要用 --out 指定 PNG 输出路径。");
                    PatchEngine.GeneratePreview(game, regular, bold, previewOutput, log);
                }
                else if (action == "apply") PatchEngine.Apply(game, regular, bold, log);
                else PatchEngine.Restore(game, log);
                log("完成。");
                return 0;
            }
            catch (Exception e)
            {
                try { Console.Error.WriteLine(e.Message); } catch { }
                if (writer != null) writer.WriteLine("错误：" + e.Message);
                return e is ArgumentException ? 2 : 1;
            }
            finally { if (writer != null) writer.Dispose(); }
        }

        private static void ValidateLogPath(string path, string game, string regular, string bold, string previewOutput)
        {
            if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
                throw new ArgumentException("日志请使用本地普通文件路径，不能使用网络路径、设备路径或备用数据流。");
            string gameRoot = game.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(path, gameRoot, StringComparison.OrdinalIgnoreCase) || path.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("日志不能写入游戏目录或备份目录。请选用独立的日志文件夹。");
            if (String.Equals(path, regular, StringComparison.OrdinalIgnoreCase) ||
                (!String.IsNullOrWhiteSpace(bold) && String.Equals(path, bold, StringComparison.OrdinalIgnoreCase)) ||
                (previewOutput != null && String.Equals(path, previewOutput, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("日志路径不能与字体文件或预览输出路径相同。");
            RejectReparseAncestors(path);
            if (File.Exists(path) || Directory.Exists(path))
                throw new ArgumentException("日志文件已存在，拒绝覆盖。请用 --log 指定新的文件名。");
        }

        private static void RejectReparseAncestors(string path)
        {
            string current = path;
            while (!String.IsNullOrWhiteSpace(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("日志路径包含链接或重定向目录，已停止写入。");
                string parent = Path.GetDirectoryName(current);
                if (String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
        }

        private static string Usage()
        {
            return "Usage:\r\n" +
                "  AoEDE-Font-Fix.exe --inspect --game \"D:\\SteamLibrary\\steamapps\\common\\AoEDE\" --log inspect.txt\r\n" +
                "  AoEDE-Font-Fix.exe --preview --game <directory> --out preview.png [--regular font.ttf] [--bold bold.ttf] [--log file]\r\n" +
                "  AoEDE-Font-Fix.exe --apply --game <directory> [--regular font.ttf] [--bold bold.ttf] [--log file]\r\n" +
                "  AoEDE-Font-Fix.exe --restore --game <directory> [--log file]\r\n\r\n" +
                "No arguments opens the graphical interface. Custom fonts must be static TrueType .ttf files.\r\n" +
                "Exit codes: 0=success, 1=operation failed, 2=invalid arguments, 3=unsupported installation.\r\n" +
                "Close the game before applying or restoring. Unknown resource versions are never overwritten.\r\n" +
                "Log files must be new local files outside the game directory; existing log files are never overwritten.";
        }
    }
}
