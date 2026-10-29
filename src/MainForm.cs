using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AoeFontRepair
{
    internal sealed class MainForm : Form
    {
        private readonly Color accent = Color.FromArgb(31, 90, 144);
        private readonly Color muted = Color.FromArgb(87, 100, 115);
        private TextBox gamePath, regularPath, boldPath, logBox;
        private CheckBox customFont;
        private Button browseGame, browseRegular, browseBold, inspectButton, previewButton, applyButton, restoreButton;
        private Label statusLabel, detailsLabel, previewHint;
        private PictureBox previewImage;
        private ProgressBar progress;
        private Inspection inspection;
        private bool busy;
        private string previewFile;

        internal MainForm()
        {
            Text = Program.ProductName + "  " + Program.Version;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(245, 247, 250);
            ForeColor = Color.FromArgb(30, 42, 54);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 720);
            MinimumSize = new Size(850, 650);
            BuildInterface();
            gamePath.TextChanged += delegate { inspection = null; ClearPreview(); SetStatus("请检查游戏目录", "选择游戏目录后，先检查文件是否兼容。", false); UpdateControls(); };
            regularPath.TextChanged += delegate { ClearPreview(); UpdateControls(); };
            boldPath.TextChanged += delegate { ClearPreview(); UpdateControls(); };
            customFont.CheckedChanged += delegate { ClearPreview(); UpdateControls(); };
            FormClosing += OnClosing;
            Shown += delegate
            {
                Rectangle workArea = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, workArea.Width - 24), Math.Min(Height, workArea.Height - 24));
                Location = new Point(workArea.Left + Math.Max(0, (workArea.Width - Width) / 2), workArea.Top + Math.Max(0, (workArea.Height - Height) / 2));
                try
                {
                    string detected = PatchEngine.DetectGameDirectory();
                    if (!String.IsNullOrWhiteSpace(detected)) { gamePath.Text = detected; InspectGame(); }
                    else SetStatus("选择游戏目录", "例如 Steam 安装目录中的 steamapps\\common\\AoEDE。", false);
                }
                catch (Exception e) { AppendLog("自动查找未完成：" + e.Message); }
            };
            UpdateControls();
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill; root.Padding = new Padding(20, 12, 20, 12); root.ColumnCount = 1; root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill; header.ColumnCount = 1; header.RowCount = 2; header.Margin = Padding.Empty;
            Label title = new Label { Text = "让汉字显示得更熟悉", AutoSize = true, ForeColor = accent, Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), Margin = Padding.Empty };
            Label subtitle = new Label { Text = "帝国时代：决定版（第一代） · Steam 版 · 非官方开源字体修复", AutoSize = true, ForeColor = muted, Margin = new Padding(1, 5, 0, 0) };
            header.Controls.Add(title, 0, 0); header.Controls.Add(subtitle, 0, 1); root.Controls.Add(header, 0, 0);

            TableLayoutPanel game = Card(3, 2);
            game.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98)); game.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); game.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            game.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); game.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            game.Controls.Add(FieldLabel("游戏目录"), 0, 0);
            gamePath = Input(); game.Controls.Add(gamePath, 1, 0);
            browseGame = MakeButton("选择目录…", false); browseGame.Click += delegate { ChooseGame(); }; game.Controls.Add(browseGame, 2, 0);
            Label gameNote = Note("选择包含 AoEDE_s.exe 的文件夹。仅处理已识别的字体资源；请先关闭游戏。");
            game.Controls.Add(gameNote, 1, 1); game.SetColumnSpan(gameNote, 2); root.Controls.Add(game, 0, 1);

            TableLayoutPanel fonts = Card(3, 4);
            fonts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98)); fonts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fonts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            fonts.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); fonts.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); fonts.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); fonts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            customFont = new CheckBox { Text = "使用自选字体（需有字体转换许可）", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            fonts.Controls.Add(customFont, 0, 0); fonts.SetColumnSpan(customFont, 2);
            fonts.Controls.Add(FieldLabel("常规字体"), 0, 1); regularPath = Input(); fonts.Controls.Add(regularPath, 1, 1);
            browseRegular = MakeButton("选择 TTF…", false); browseRegular.Click += delegate { ChooseFont(regularPath); }; fonts.Controls.Add(browseRegular, 2, 1);
            fonts.Controls.Add(FieldLabel("粗体（可选）"), 0, 2); boldPath = Input(); fonts.Controls.Add(boldPath, 1, 2);
            browseBold = MakeButton("选择 TTF…", false); browseBold.Click += delegate { ChooseFont(boldPath); }; fonts.Controls.Add(browseBold, 2, 2);
            Label fontNote = Note("默认：思源黑体简体中文（Aoe CJK Sans，OFL 衍生版）。自选限静态 TTF；未选粗体时沿用常规字重。");
            fonts.Controls.Add(fontNote, 0, 3); fonts.SetColumnSpan(fontNote, 3); root.Controls.Add(fonts, 0, 2);

            TableLayoutPanel operation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 8, 0, 8) };
            operation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); operation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            operation.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); operation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusLabel = new Label { AutoSize = true, Text = "选择游戏目录", Font = new Font(Font, FontStyle.Bold), ForeColor = accent, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            detailsLabel = new Label { Dock = DockStyle.Fill, Text = "安装前会备份原文件；恢复功能会核对文件，保护游戏更新和其他修改。", ForeColor = muted, AutoEllipsis = true, Margin = new Padding(0, 4, 10, 0) };
            operation.Controls.Add(statusLabel, 0, 0); operation.Controls.Add(detailsLabel, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            inspectButton = MakeButton("检查目录", false); inspectButton.Click += delegate { InspectGame(); };
            previewButton = MakeButton("预览效果", false); previewButton.Click += delegate { Preview(); };
            applyButton = MakeButton("应用字体", true); applyButton.Click += delegate { Apply(); };
            restoreButton = MakeButton("恢复原样", false); restoreButton.Click += delegate { Restore(); };
            buttons.Controls.Add(inspectButton); buttons.Controls.Add(previewButton); buttons.Controls.Add(applyButton); buttons.Controls.Add(restoreButton);
            operation.Controls.Add(buttons, 1, 0); operation.SetRowSpan(buttons, 2); root.Controls.Add(operation, 0, 3);

            Panel preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 10), BorderStyle = BorderStyle.FixedSingle };
            previewImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
            previewHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = muted, Text = "先检查目录，再点击「预览效果」\r\n\r\n设置 · 房间 · 希腊人 · 腓尼基人 · 巴比伦人", Font = new Font(Font.FontFamily, 12F) };
            preview.Controls.Add(previewImage); preview.Controls.Add(previewHint); root.Controls.Add(preview, 0, 4);

            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ForeColor = muted, Margin = Padding.Empty };
            root.Controls.Add(logBox, 0, 5);
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            footer.Controls.Add(Note("v" + Program.Version + "  ·  离线运行  ·  本机生成  ·  备份与恢复"), 0, 0);
            progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Visible = false, Margin = new Padding(0, 2, 0, 2) };
            footer.Controls.Add(progress, 1, 0); root.Controls.Add(footer, 0, 6);
        }

        private TableLayoutPanel Card(int columns, int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = columns, RowCount = rows, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 10) };
        }

        private Label FieldLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 6, 0) };
        }

        private Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = muted, Margin = new Padding(0, 2, 0, 0), Font = new Font(Font.FontFamily, 8.5F) };
        }

        private TextBox Input()
        {
            return new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 4, 8, 3) };
        }

        private Button MakeButton(string text, bool primary)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 32), Height = 34, FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : accent, Padding = new Padding(7, 2, 7, 2), Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(201, 211, 222);
            return button;
        }

        private void ChooseGame()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含 AoEDE_s.exe 的游戏根目录";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(gamePath.Text)) dialog.SelectedPath = gamePath.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) { gamePath.Text = dialog.SelectedPath; InspectGame(); }
            }
        }

        private void ChooseFont(TextBox target)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择允许转换的静态 TrueType 字体";
                dialog.Filter = "静态 TrueType 字体 (*.ttf)|*.ttf";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
            }
        }

        private string RegularFont { get { return customFont.Checked ? regularPath.Text.Trim() : Program.BundledFont("Regular"); } }
        private string BoldFont { get { return customFont.Checked ? boldPath.Text.Trim() : Program.BundledFont("Bold"); } }

        private bool ValidateFontInput()
        {
            if (!File.Exists(RegularFont)) { SetStatus("找不到常规字体文件", "请完整解压下载包，或重新选择常规 TTF 文件。", true); return false; }
            if (!String.IsNullOrWhiteSpace(BoldFont) && !File.Exists(BoldFont)) { SetStatus("找不到粗体字体文件", "请重新选择粗体 TTF 文件，或清空此项以沿用常规字重。", true); return false; }
            return true;
        }

        private void InspectGame()
        {
            if (busy || String.IsNullOrWhiteSpace(gamePath.Text)) return;
            string game = gamePath.Text.Trim();
            Run("正在检查游戏字体资源…", delegate { return PatchEngine.Inspect(game); }, delegate(object result) { ShowInspection((Inspection)result); });
        }

        private void Preview()
        {
            if (!ValidateFontInput()) return;
            string game = gamePath.Text.Trim(), regular = RegularFont, bold = BoldFont;
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AoEDE-Chinese-Font-Fix", "previews");
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
            Run("正在生成字体预览…", delegate
            {
                PatchEngine.GeneratePreview(game, regular, bold, output, AppendLog);
                return output;
            }, delegate(object result)
            {
                ClearPreview();
                previewFile = (string)result;
                using (FileStream stream = File.OpenRead(previewFile))
                using (Image image = Image.FromStream(stream)) previewImage.Image = new Bitmap(image);
                previewHint.Visible = false;
                SetStatus("预览已生成", "确认字形后点击「应用字体」。预览不会修改游戏。", false);
            });
        }

        private void Apply()
        {
            if (!ValidateFontInput()) return;
            string game = gamePath.Text.Trim(), regular = RegularFont, bold = BoldFont;
            Run("正在生成并安装字体，请稍候…", delegate
            {
                PatchEngine.Apply(game, regular, bold, AppendLog);
                return PatchEngine.Inspect(game);
            }, delegate(object result)
            {
                ShowInspection((Inspection)result);
                SetStatus("字体已应用", "重新启动游戏即可查看。需要撤销时，点击「恢复原样」。", false);
            });
        }

        private void Restore()
        {
            string game = gamePath.Text.Trim();
            Run("正在核对备份并恢复…", delegate
            {
                PatchEngine.Restore(game, AppendLog);
                return PatchEngine.Inspect(game);
            }, delegate(object result)
            {
                ShowInspection((Inspection)result);
                SetStatus("已恢复原样", "已恢复本工具备份的原始字体资源。", false);
                ClearPreview();
            });
        }

        private void Run(string message, Func<object> operation, Action<object> completed)
        {
            if (busy) return;
            busy = true;
            SetStatus(message, "处理期间请保持游戏关闭。详细进度显示在下方。", false);
            AppendLog(message); UpdateControls();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = operation(); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                try
                {
                    if (e.Error != null)
                    {
                        AppendLog("操作未完成：" + e.Error.Message);
                        try { inspection = PatchEngine.Inspect(gamePath.Text.Trim()); } catch { inspection = null; }
                        SetStatus("操作未完成", e.Error.Message, true);
                    }
                    else completed(e.Result);
                }
                catch (Exception error) { AppendLog("显示结果时出现问题：" + error.Message); SetStatus("请查看操作记录", error.Message, true); }
                finally { UpdateControls(); worker.Dispose(); }
            };
            worker.RunWorkerAsync();
        }

        private void ShowInspection(Inspection value)
        {
            inspection = value;
            SetStatus(value.Status, value.Details, !value.CanApply && !value.CanRestore);
            AppendLog(value.Status + "\r\n" + value.Details);
        }

        private void SetStatus(string title, string description, bool error)
        {
            statusLabel.Text = title;
            statusLabel.ForeColor = error ? Color.FromArgb(163, 63, 46) : accent;
            detailsLabel.Text = description;
        }

        private void AppendLog(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), text); return; }
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }

        private void UpdateControls()
        {
            gamePath.Enabled = browseGame.Enabled = customFont.Enabled = !busy;
            regularPath.Enabled = boldPath.Enabled = browseRegular.Enabled = browseBold.Enabled = !busy && customFont.Checked;
            inspectButton.Enabled = !busy && !String.IsNullOrWhiteSpace(gamePath.Text);
            previewButton.Enabled = !busy && inspection != null && inspection.CanApply;
            applyButton.Enabled = !busy && inspection != null && inspection.CanApply;
            restoreButton.Enabled = !busy && inspection != null && inspection.CanRestore;
            progress.Visible = busy; progress.MarqueeAnimationSpeed = busy ? 25 : 0;
            UseWaitCursor = busy;
        }

        private void ClearPreview()
        {
            if (previewImage.Image != null) { Image old = previewImage.Image; previewImage.Image = null; old.Dispose(); }
            previewHint.Visible = true;
            if (previewFile != null)
            {
                try { File.Delete(previewFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                previewFile = null;
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                SetStatus("正在处理，请稍候", "为了完整保存或恢复文件，请等当前操作结束后再关闭。", false);
                return;
            }
            ClearPreview();
        }
    }
}
