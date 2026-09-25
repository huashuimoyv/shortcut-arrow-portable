using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("快捷方式箭头")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

public class SavedSetting
{
    public bool KeyExisted;
    public bool ValueExisted;
    public string Value;
    public int Kind;
}

public sealed class ArrowSettings
{
    public const string SystemKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    readonly RegistryKey root;
    readonly string keyPath, dataPath;
    string BackupPath { get { return Path.Combine(dataPath, "previous.xml"); } }
    string IconPath { get { return Path.Combine(dataPath, "transparent-v1.ico"); } }
    string ManagedValue { get { return IconPath + ",0"; } }

    public ArrowSettings(RegistryKey root, string keyPath, string dataPath)
    { this.root = root; this.keyPath = keyPath; this.dataPath = dataPath; }

    public SavedSetting Read()
    {
        using (var k = root.OpenSubKey(keyPath))
        {
            var s = new SavedSetting { KeyExisted = k != null };
            if (k == null) return s;
            object v = k.GetValue("29", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            s.ValueExisted = v != null;
            if (s.ValueExisted)
            {
                var kind = k.GetValueKind("29");
                if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
                    throw new InvalidOperationException("现有箭头设置不是文本类型，未作修改。");
                s.Kind = (int)kind;
                s.Value = (string)v;
            }
            return s;
        }
    }

    bool Same(SavedSetting a, SavedSetting b)
    { return a.ValueExisted == b.ValueExisted && (!a.ValueExisted || (a.Kind == b.Kind && a.Value == b.Value)); }

    public bool IsHidden { get { var s = Read(); return s.ValueExisted && s.Value == ManagedValue; } }
    public bool HasBackup { get { return File.Exists(BackupPath); } }

    SavedSetting LoadBackup()
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = System.Xml.XmlReader.Create(BackupPath, settings))
        {
            var saved = (SavedSetting)new XmlSerializer(typeof(SavedSetting)).Deserialize(reader);
            if (saved.ValueExisted && (saved.Value == null ||
                (saved.Kind != (int)RegistryValueKind.String && saved.Kind != (int)RegistryValueKind.ExpandString)))
                throw new InvalidDataException("原设置备份损坏，未作修改。");
            return saved;
        }
    }

    public void Hide()
    {
        var current = Read();
        Directory.CreateDirectory(dataPath);
        if (HasBackup)
        {
            var saved = LoadBackup();
            if (!IsHidden && !Same(current, saved))
                throw new InvalidOperationException("箭头设置已被其他工具修改。为保留这些更改，本次未覆盖。");
        }
        else
        {
            if (IsHidden) throw new InvalidOperationException("找不到原设置备份，未继续修改。");
            string temp = BackupPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                {
                    new XmlSerializer(typeof(SavedSetting)).Serialize(stream, current);
                    stream.Flush(true);
                }
                File.Move(temp, BackupPath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        // Keep the asset outside the portable EXE location, so moving the EXE is safe.
        if (!File.Exists(IconPath)) File.WriteAllBytes(IconPath, BlankIcon());
        using (var k = root.CreateSubKey(keyPath)) k.SetValue("29", ManagedValue, RegistryValueKind.String);
        if (!IsHidden) throw new IOException("设置写入后验证失败。");
    }

    public void Restore()
    {
        if (!HasBackup) throw new InvalidOperationException("尚无本工具的备份，无需恢复。");
        var saved = LoadBackup();
        var current = Read();
        if (!IsHidden && !Same(current, saved))
            throw new InvalidOperationException("箭头设置已被其他工具修改。为保留这些更改，本次未覆盖。");
        using (var k = root.OpenSubKey(keyPath, true))
        {
            if (k != null)
            {
                if (saved.ValueExisted) k.SetValue("29", saved.Value, (RegistryValueKind)saved.Kind);
                else k.DeleteValue("29", false);
            }
            else if (saved.ValueExisted)
            {
                using (var created = root.CreateSubKey(keyPath))
                    created.SetValue("29", saved.Value, (RegistryValueKind)saved.Kind);
            }
        }
        if (!Same(Read(), saved)) throw new IOException("恢复后验证失败，备份已保留。");
        if (!saved.KeyExisted)
        {
            bool empty;
            using (var k = root.OpenSubKey(keyPath)) empty = k != null && k.ValueCount == 0 && k.SubKeyCount == 0;
            if (empty) root.DeleteSubKey(keyPath, false);
        }
        File.Delete(BackupPath);
        // Keep the tiny icon asset available until Explorer releases its old setting.
    }

    public static byte[] BlankIcon()
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128 };
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            foreach (int n in sizes)
            {
                int length = 40 + n * n * 4 + ((n + 31) / 32 * 4) * n;
                w.Write((byte)(n == 256 ? 0 : n)); w.Write((byte)(n == 256 ? 0 : n));
                w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
                w.Write(length); w.Write(offset); offset += length;
            }
            foreach (int n in sizes)
            {
                int maskSize = ((n + 31) / 32 * 4) * n;
                w.Write(40); w.Write(n); w.Write(n * 2); w.Write((ushort)1); w.Write((ushort)32);
                w.Write(0); w.Write(n * n * 4 + maskSize);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                w.Write(new byte[n * n * 4]);
                for (int i = 0; i < maskSize; i++) w.Write((byte)255);
            }
            return ms.ToArray();
        }
    }
}

static class Program
{
    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint e, uint flags, IntPtr a, IntPtr b);
    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    public static RegistryKey MachineRoot()
    { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32); }
    public static ArrowSettings Store(RegistryKey root)
    { return new ArrowSettings(root, ArrowSettings.SystemKey, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ShortcutArrowPortable")); }
    public static void Refresh() { SHChangeNotify(0x08000000, 0x2000, IntPtr.Zero, IntPtr.Zero); }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Length == 2 && args[0] == "--self-test") { SelfTest(args[1]); return 0; }
            if ((args.Length == 2 || args.Length == 3) && args[0] == "--round-link")
            { new IndividualIcon(args[1]).ApplyRounded(args.Length == 3 && args[2] == "--clean"); return 0; }
            if (args.Length == 3 && args[0] == "--preview-single")
            {
                using (var f = new SingleIconForm(args[1]))
                {
                    f.ShowInTaskbar=false; f.Opacity=0; f.Show(); Application.DoEvents();
                    using (var b=new Bitmap(f.Width,f.Height)) { f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size)); b.Save(args[2]); }
                    f.Close();
                }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                using (var f = new MainForm())
                {
                    f.ShowInTaskbar = false; f.Opacity = 0; f.Show(); Application.DoEvents();
                    using (var b = new Bitmap(f.Width, f.Height))
                    { f.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size)); b.Save(args[1]); }
                    f.Close();
                }
                return 0;
            }
            if (args.Length == 1 && (args[0] == "--hide" || args[0] == "--restore"))
            {
                bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                if (!admin) throw new InvalidOperationException("请通过程序窗口操作，并允许管理员权限请求。");
                using (var mutex = new Mutex(false, @"Global\ShortcutArrowPortable.Change.v1"))
                {
                    bool held = false;
                    try
                    {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new InvalidOperationException("另一个窗口正在修改设置，请稍后再试。");
                        using (var root = MachineRoot())
                        { var s = Store(root); if (args[0] == "--hide") s.Hide(); else s.Restore(); }
                    }
                    finally { if (held) mutex.ReleaseMutex(); }
                }
                return 0;
            }
            if(args.Length > 0)
            {
                if(args.Length != 1) throw new InvalidOperationException("一次只拖入一个快捷方式。");
                Application.Run(new SingleIconForm(args[0]));
            }
            else Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 2 && args[0] == "--self-test") File.WriteAllText(args[1], "FAIL\r\n" + ex);
            else MessageBox.Show(ex.Message, "快捷方式箭头", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static async Task RestartDesktop()
    {
        uint pid;
        if (GetShellWindow() == IntPtr.Zero) throw new InvalidOperationException("没有找到当前桌面，请注销后重新登录。");
        GetWindowThreadProcessId(GetShellWindow(), out pid);
        using (var p = Process.GetProcessById((int)pid))
        {
            if (p.ProcessName != "explorer" || p.SessionId != Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("无法确认当前桌面进程，请注销后重新登录。");
            p.Kill();
            await Task.Run(() => p.WaitForExit(5000));
        }
        // Windows normally restarts its shell itself; only launch it if needed.
        for (int i = 0; i < 16 && GetShellWindow() == IntPtr.Zero; i++) await Task.Delay(250);
        if (GetShellWindow() == IntPtr.Zero)
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
        for (int i = 0; i < 24 && GetShellWindow() == IntPtr.Zero; i++) await Task.Delay(250);
        if (GetShellWindow() == IntPtr.Zero) throw new IOException("桌面尚未恢复，请用任务管理器运行 explorer.exe，或注销后重新登录。");
        Refresh();
    }

    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void SelfTest(string report)
    {
        string id = Guid.NewGuid().ToString("N");
        string key = @"Software\ShortcutArrowPortable.Tests\" + id;
        string folder = Path.Combine(Path.GetTempPath(), "ShortcutArrowPortable-Test-" + id);
        int passed = 0;
        using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
        {
            try
            {
                var store = new ArrowSettings(root, key, folder);
                store.Hide(); Check(store.IsHidden && store.HasBackup, "hide missing value"); passed++;
                store.Hide(); store.Restore(); Check(!store.Read().KeyExisted && !store.HasBackup, "repeat hide / restore absent key"); passed++;
                using (var k = root.CreateSubKey(key)) { k.SetValue("29", @"%SystemRoot%\original.ico,3", RegistryValueKind.ExpandString); k.SetValue("other", "keep"); }
                store.Hide(); store.Restore();
                var original = store.Read();
                Check(original.Value == @"%SystemRoot%\original.ico,3" && original.Kind == (int)RegistryValueKind.ExpandString, "preserve original type and value"); passed++;
                using (var k = root.OpenSubKey(key)) Check((string)k.GetValue("other") == "keep", "preserve unrelated value"); passed++;
                store.Hide();
                using (var k = root.OpenSubKey(key, true)) k.SetValue("29", "external.ico,0");
                bool refused = false;
                try { store.Restore(); } catch (InvalidOperationException) { refused = true; }
                Check(refused && store.HasBackup && store.Read().Value == "external.ico,0", "external changes protected"); passed++;
                refused = false;
                try { store.Hide(); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "hide respects external changes"); passed++;
                using (var k = root.OpenSubKey(key, true)) k.SetValue("29", @"%SystemRoot%\original.ico,3", RegistryValueKind.ExpandString);
                store.Restore(); Check(!store.HasBackup, "recover already-restored state"); passed++;
                using (var k = root.OpenSubKey(key, true)) k.SetValue("29", 123, RegistryValueKind.DWord);
                refused = false;
                try { store.Hide(); } catch (InvalidOperationException) { refused = true; }
                Check(refused && !store.HasBackup, "unsupported value type protected"); passed++;
                foreach (int n in new[] {16,24,32,48,64,128})
                using (var stream = new MemoryStream(ArrowSettings.BlankIcon()))
                using (var icon = new Icon(stream, n, n))
                using (var bitmap = icon.ToBitmap())
                {
                    Check(bitmap.Width == n && bitmap.Height == n, "icon size: requested " + n + ", actual " + bitmap.Width);
                    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) Check(bitmap.GetPixel(x,y).A == 0, "icon transparency");
                    passed++;
                }
                File.WriteAllText(report, "PASS: " + passed + " checks. Isolated HKCU test key only; production arrow setting unchanged.\r\n");
            }
            finally
            {
                root.DeleteSubKeyTree(key, false);
                string resolved = Path.GetFullPath(folder);
                if (resolved == Path.Combine(Path.GetFullPath(Path.GetTempPath()), "ShortcutArrowPortable-Test-" + id) && Directory.Exists(resolved))
                    Directory.Delete(resolved, true);
            }
        }
    }
}

sealed class MainForm : Form
{
    readonly Label status;
    readonly Button hide, restore;
    readonly LinkLabel restart;
    bool busy;
    public MainForm()
    {
        Text = "快捷方式箭头";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(408, 330);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(250,251,252);
        using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon"))
        using (var embeddedIcon = new Icon(iconStream, 32, 32))
            Icon = (Icon)embeddedIcon.Clone();
        Controls.Add(new Label { Text = "快捷方式箭头", Font = new Font(Font.FontFamily, 19, FontStyle.Bold), Location = new Point(24,22), Size = new Size(360,39) });
        Controls.Add(new Label { Text = "全局设置：去除所有快捷方式的小箭头。", ForeColor = Color.FromArgb(95,103,113), Location = new Point(25,69), Size = new Size(360,26) });
        status = new Label { Location = new Point(25,108), Size = new Size(360,28), ForeColor = Color.FromArgb(46,90,77) };
        Controls.Add(status);
        hide = MakeButton("去除箭头", 25, true);
        restore = MakeButton("恢复原样", 214, false);
        hide.Click += async (s,e) => await Change("--hide");
        restore.Click += async (s,e) => await Change("--restore");
        restart = new LinkLabel { Text = "重启桌面", Location = new Point(25,203), Size = new Size(88,25), LinkColor = Color.FromArgb(41,104,84), ActiveLinkColor = Color.FromArgb(27,76,62) };
        restart.LinkClicked += async (s,e) =>
        {
            if (MessageBox.Show(this, "桌面和任务栏会短暂消失，文件夹窗口可能关闭。\n请先完成文件复制或移动操作。\n\n现在重启桌面？", "重启桌面", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SetBusy(true);
            try { await Program.RestartDesktop(); status.Text = "桌面已重启，请查看图标。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
        };
        Controls.Add(restart);
        Controls.Add(new Label { Text = "未生效时使用，也可注销后重新登录", Font = new Font(Font.FontFamily, 9F), Location = new Point(112,205), Size = new Size(280,23), ForeColor = Color.Gray });
        Controls.Add(new Label { Text = "全机生效 · 修改时需管理员权限 · 无需安装", Font = new Font(Font.FontFamily, 8.5F), Location = new Point(25,239), Size = new Size(365,22), ForeColor = Color.Gray });
        var single=new LinkLabel{Text="单个图标：拖入快捷方式，或点击选择",Location=new Point(25,285),Size=new Size(360,25),LinkColor=Color.FromArgb(41,104,84)};
        single.LinkClicked+=(s,e)=>
        {
            if(busy)return;
            using(var picker=new OpenFileDialog{Title="选择要处理的快捷方式",Filter="Windows 快捷方式 (*.lnk)|*.lnk",DereferenceLinks=false})
                if(picker.ShowDialog(this)==DialogResult.OK) OpenSingle(picker.FileName);
        };
        Controls.Add(single);
        EnableDrop(this);
        UpdateStatus();
        // Point fonts already follow system DPI; scale control geometry exactly once.
        using (var graphics = CreateGraphics())
        {
            float factor = graphics.DpiX / 96F;
            if (Math.Abs(factor - 1F) > 0.01F)
            {
                foreach (Control control in Controls)
                    control.Bounds = new Rectangle((int)Math.Round(control.Left * factor), (int)Math.Round(control.Top * factor),
                        (int)Math.Round(control.Width * factor), (int)Math.Round(control.Height * factor));
                ClientSize = new Size((int)Math.Round(408 * factor), (int)Math.Round(330 * factor));
            }
        }
        FormClosing += (s,e) => { if (busy) e.Cancel = true; };
    }

    void OpenSingle(string path)
    {
        if(busy)return;
        try{using(var form=new SingleIconForm(path))form.ShowDialog(this);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    void EnableDrop(Control control)
    {
        control.AllowDrop=true;
        control.DragEnter+=(s,e)=>
        {
            var files=e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Effect=!busy&&files!=null&&files.Length==1&&String.Equals(Path.GetExtension(files[0]),".lnk",StringComparison.OrdinalIgnoreCase)?DragDropEffects.Copy:DragDropEffects.None;
        };
        control.DragDrop+=(s,e)=>{var files=e.Data.GetData(DataFormats.FileDrop)as string[];if(files!=null&&files.Length==1)OpenSingle(files[0]);};
        foreach(Control child in control.Controls)EnableDrop(child);
    }

    Button MakeButton(string text, int left, bool primary)
    {
        var b = new Button { Text = text, Location = new Point(left,148), Size = new Size(169,42), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Color.FromArgb(41,104,84) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(49,57,66) };
        b.FlatAppearance.BorderColor = primary ? b.BackColor : Color.FromArgb(218,222,226);
        Controls.Add(b); return b;
    }
    void UpdateStatus()
    {
        try
        {
            using (var root = Program.MachineRoot())
            {
                var s = Program.Store(root);
                status.Text = s.IsHidden ? "已设置：去除箭头" : "当前：" + (s.Read().ValueExisted ? "已有自定义设置" : "默认箭头");
                restore.Enabled = s.HasBackup;
            }
        }
        catch { status.Text = "暂时无法读取设置"; restore.Enabled = false; }
    }
    void SetBusy(bool value) { busy = value; hide.Enabled = restart.Enabled = !value; restore.Enabled = !value; }
    async Task Change(string action)
    {
        SetBusy(true);
        bool success = false;
        try
        {
            using (var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, action) { UseShellExecute = true, Verb = "runas" }))
            {
                await Task.Run(() => p.WaitForExit());
                success = p.ExitCode == 0;
            }
            if (success) Program.Refresh();
        }
        catch (Win32Exception ex)
        { if (ex.NativeErrorCode != 1223) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            SetBusy(false); UpdateStatus();
            if (success) status.Text = action == "--hide" ? "已设置去除箭头；未生效请重启桌面。" : "已恢复原设置；未生效请重启桌面。";
        }
    }
}
