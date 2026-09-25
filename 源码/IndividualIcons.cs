using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.CSharp;

public class LinkIconBackup
{
    public string OriginalIcon;
    public string AppliedIcon;
    public string PreviousAppliedIcon;
    public bool CleanBackground;
}

public sealed class IndividualIcon
{
    public readonly string LinkPath;
    readonly string dataDirectory;
    string BackupFile { get { return Path.Combine(dataDirectory, "icon-setting.xml"); } }
    public bool CanRestore { get { return File.Exists(BackupFile); } }
    public bool CurrentClean { get { return CanRestore && LoadBackup().CleanBackground; } }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(uint e, uint flags, string path, IntPtr unused);

    public IndividualIcon(string path, string storage = null)
    {
        LinkPath = Path.GetFullPath(path);
        if (!File.Exists(LinkPath) || !String.Equals(Path.GetExtension(LinkPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请拖入一个 Windows 快捷方式（.lnk），不要拖入程序本体。");
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(LinkPath.ToUpperInvariant()))).Replace("-", "");
        dataDirectory = Path.Combine(storage ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShortcutArrowPortable", "Icons"), hash);
    }

    static object Get(object obj, string name)
    { return obj.GetType().InvokeMember(name, BindingFlags.GetProperty, null, obj, null); }
    public static string Property(string path, string name)
    {
        object shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            return Convert.ToString(Get(link, name));
        }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }
    public static void SetIcon(string path, string value)
    {
        object shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            link.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { value });
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        SHChangeNotify(0x2000, 0x2005, path, IntPtr.Zero);
    }

    LinkIconBackup LoadBackup()
    {
        using (var reader = System.Xml.XmlReader.Create(BackupFile, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null }))
            return (LinkIconBackup)new XmlSerializer(typeof(LinkIconBackup)).Deserialize(reader);
    }
    void SaveBackup(LinkIconBackup data)
    {
        string temp = BackupFile + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            { new XmlSerializer(typeof(LinkIconBackup)).Serialize(stream, data); stream.Flush(true); }
            if (File.Exists(BackupFile)) File.Replace(temp, BackupFile, null);
            else File.Move(temp, BackupFile);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public Bitmap ReadIcon(bool original)
    {
        string location = original && CanRestore ? LoadBackup().OriginalIcon : Property(LinkPath, "IconLocation");
        string file = location;
        int index = 0, comma = location.LastIndexOf(',');
        if (comma >= 0 && Int32.TryParse(location.Substring(comma + 1).Trim(), out index)) file = location.Substring(0, comma);
        file = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
        if (String.IsNullOrEmpty(file)) file = Property(LinkPath, "TargetPath");
        if (!Path.IsPathRooted(file)) file = Path.Combine(Path.GetDirectoryName(LinkPath), file);
        if (!File.Exists(file)) throw new FileNotFoundException("找不到图标来源：" + file);
        if (String.Equals(Path.GetExtension(file), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            // Prefer the largest PNG frame; this avoids legacy Icon's size limitations.
            byte[] bytes = File.ReadAllBytes(file);
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) throw new InvalidDataException("ICO 格式无效。");
                int count = reader.ReadUInt16(), bestSize = 0, bestOffset = 0, bestLength = 0;
                for (int i = 0; i < count; i++)
                {
                    int width = reader.ReadByte(); if (width == 0) width = 256;
                    reader.ReadBytes(7);
                    int length = reader.ReadInt32(), offset = reader.ReadInt32();
                    if (offset < 0 || length < 1 || (long)offset + length > bytes.Length) throw new InvalidDataException("ICO 数据不完整。");
                    if (width > bestSize && bytes[offset] == 137)
                    { bestSize = width; bestOffset = offset; bestLength = length; }
                }
                if (bestSize > 0)
                using (var stream = new MemoryStream(bytes, bestOffset, bestLength))
                using (var image = new Bitmap(stream)) return new Bitmap(image);
            }
        }
        var icons = new IntPtr[1];
        uint result = PrivateExtractIcons(file, index, 256, 256, icons, new uint[1], 1, 0);
        if (result > 0 && result != UInt32.MaxValue && icons[0] != IntPtr.Zero)
        {
            try { using (var icon = Icon.FromHandle(icons[0])) return icon.ToBitmap(); }
            finally { DestroyIcon(icons[0]); }
        }
        using (var fallback = Icon.ExtractAssociatedIcon(file))
        { if (fallback == null) throw new IOException("无法读取该文件的图标。"); return fallback.ToBitmap(); }
    }

    public static Bitmap Rounded(Bitmap source, bool cleanLightBackground)
    {
        var output = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(output))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, new Rectangle(0, 0, 256, 256));
        }
        const double radius = 48;
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        {
            Color c = output.GetPixel(x,y);
            int hi = Math.Max(c.R, Math.Max(c.G,c.B)), lo = Math.Min(c.R, Math.Min(c.G,c.B));
            if (cleanLightBackground && lo >= 175 && hi - lo <= 14) c = Color.FromArgb(c.A, 255, 251, 241);
            double dx = Math.Max(Math.Max(radius - (x + 0.5), (x + 0.5) - (256 - radius)), 0);
            double dy = Math.Max(Math.Max(radius - (y + 0.5), (y + 0.5) - (256 - radius)), 0);
            double coverage = Math.Max(0, Math.Min(1, radius + 0.5 - Math.Sqrt(dx*dx+dy*dy)));
            output.SetPixel(x,y, Color.FromArgb((int)Math.Round(c.A * coverage), c.R,c.G,c.B));
        }
        return output;
    }

    public static byte[] EncodeIcon(Bitmap bitmap)
    {
        int[] sizes = {16,20,24,32,40,48,64,128,256};
        var frames = new System.Collections.Generic.List<byte[]>();
        foreach (int n in sizes)
        using (var small = new Bitmap(n,n,PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(small))
        using (var frame = new MemoryStream())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bitmap, new Rectangle(0,0,n,n));
            small.Save(frame,ImageFormat.Png); frames.Add(frame.ToArray());
        }
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length*16;
            for (int i=0;i<sizes.Length;i++)
            {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset+=frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
            return output.ToArray();
        }
    }

    public void ApplyRounded(bool clean)
    {
        string current = Property(LinkPath,"IconLocation");
        var backup = CanRestore ? LoadBackup() : new LinkIconBackup { OriginalIcon = current };
        if (CanRestore && current != backup.AppliedIcon && current != backup.PreviousAppliedIcon && current != backup.OriginalIcon)
            throw new InvalidOperationException("图标已被其他程序更改，未覆盖现有设置。");
        Directory.CreateDirectory(dataDirectory);
        // Full copy is a recovery aid; normal restore changes only IconLocation.
        if (!File.Exists(Path.Combine(dataDirectory,"original.lnk"))) File.Copy(LinkPath,Path.Combine(dataDirectory,"original.lnk"));
        string iconFile = Path.Combine(dataDirectory,"rounded-"+Guid.NewGuid().ToString("N")+".ico");
        using (var source = ReadIcon(true))
        using (var result = Rounded(source,clean)) File.WriteAllBytes(iconFile,EncodeIcon(result));
        string previousApplied = backup.AppliedIcon;
        backup.PreviousAppliedIcon=previousApplied;
        backup.AppliedIcon = iconFile+",0";
        backup.CleanBackground=clean;
        SaveBackup(backup);
        try
        {
            SetIcon(LinkPath,backup.AppliedIcon);
            if(Property(LinkPath,"IconLocation")!=backup.AppliedIcon) throw new IOException("图标写入后验证失败。");
        }
        catch
        {
            // Retain a restorable record of either the old or new managed icon.
            if(Property(LinkPath,"IconLocation")==current && previousApplied!=null)
            { backup.AppliedIcon=previousApplied; SaveBackup(backup); }
            throw;
        }
    }
    public void Restore()
    {
        if(!CanRestore) throw new InvalidOperationException("没有本工具的图标备份。");
        var backup=LoadBackup(); string current=Property(LinkPath,"IconLocation");
        if(current!=backup.AppliedIcon && current!=backup.PreviousAppliedIcon && current!=backup.OriginalIcon)
            throw new InvalidOperationException("图标已被其他程序更改，未覆盖现有设置。");
        SetIcon(LinkPath,backup.OriginalIcon);
        if(Property(LinkPath,"IconLocation")!=backup.OriginalIcon) throw new IOException("恢复后验证失败，备份已保留。");
        File.Delete(BackupFile);
    }

    public void CreateLauncher(string destination, Bitmap image)
    {
        destination=Path.GetFullPath(destination);
        if(!String.Equals(Path.GetExtension(destination),".exe",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请保存为 .exe 文件。");
        if(File.Exists(destination)) throw new IOException("该文件已存在，请另选名称，避免覆盖。");
        string work=Path.Combine(Path.GetTempPath(),"ArrowLauncher-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string id=Guid.NewGuid().ToString("N");
        string staged=Path.Combine(Path.GetDirectoryName(destination),".arrow-"+id+".exe");
        try
        {
            File.Copy(LinkPath,Path.Combine(work,"launch.lnk"));
            File.WriteAllBytes(Path.Combine(work,"icon.ico"),EncodeIcon(image));
            string source=@"using System; using System.IO; using System.Diagnostics; using System.Reflection; using System.Windows.Forms;
class Launcher { [STAThread] static void Main() { try {
string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),""ShortcutArrowPortable"",""Launchers"","""+id+@""");
Directory.CreateDirectory(folder); string path=Path.Combine(folder,""launch.lnk"");
string temp=path+"".""+Guid.NewGuid().ToString(""N"");
using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(""launch.lnk""))
using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)){input.CopyTo(output);}
try { if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
catch(IOException){ if(!File.Exists(path)) throw; } finally { if(File.Exists(temp)) File.Delete(temp); }
Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
} catch(Exception e){MessageBox.Show(e.Message,""启动失败"",MessageBoxButtons.OK,MessageBoxIcon.Error);} } }";
            using(var provider=new CSharpCodeProvider())
            {
                var options=new CompilerParameters(new[]{"System.dll","System.Windows.Forms.dll"},staged);
                options.GenerateExecutable=true;
                options.CompilerOptions="/target:winexe /optimize+ /win32icon:\""+Path.Combine(work,"icon.ico")+"\"";
                options.EmbeddedResources.Add(Path.Combine(work,"launch.lnk"));
                var result=provider.CompileAssemblyFromSource(options,source);
                if(result.Errors.HasErrors)
                { var message=new StringBuilder("启动器生成失败："); foreach(CompilerError error in result.Errors) if(!error.IsWarning) message.AppendLine(error.ErrorText); throw new IOException(message.ToString()); }
            }
            File.Move(staged,destination);
            SHChangeNotify(2,0x2005,destination,IntPtr.Zero);
        }
        finally
        {
            if(File.Exists(staged)) File.Delete(staged);
            if(Path.GetFullPath(work).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(work).StartsWith("ArrowLauncher-",StringComparison.Ordinal)) Directory.Delete(work,true);
        }
    }
}

sealed class SingleIconForm : Form
{
    readonly IndividualIcon item;
    readonly PictureBox originalView, roundedView;
    readonly CheckBox clean;
    readonly Label feedback;
    readonly Button restore;
    public SingleIconForm(string path)
    {
        item=new IndividualIcon(path);
        Text="单个快捷方式"; Font=new Font("Microsoft YaHei UI",10F); AutoScaleMode=AutoScaleMode.None;
        ClientSize=new Size(488,448); StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; BackColor=Color.FromArgb(250,251,252);
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon"))
        using(var icon=new Icon(stream,32,32)) Icon=(Icon)icon.Clone();
        AddLabel(Path.GetFileNameWithoutExtension(path),24,18,440,32,14,true);
        AddLabel("只处理这个快捷方式",24,57,440,23,9,false);
        originalView=new PictureBox{Bounds=new Rectangle(71,103,112,112),SizeMode=PictureBoxSizeMode.Zoom};
        roundedView=new PictureBox{Bounds=new Rectangle(300,103,112,112),SizeMode=PictureBoxSizeMode.Zoom};
        Controls.Add(originalView); Controls.Add(roundedView);
        AddLabel("原图",107,222,90,22,9,false); AddLabel("圆角预览",327,222,110,22,9,false);
        clean=new CheckBox{Text="将浅灰 / 白色背景统一为米白色",Bounds=new Rectangle(26,257,440,25)};
        clean.Checked=item.CurrentClean;
        clean.CheckedChanged+=(s,e)=>RefreshImages(); Controls.Add(clean);
        var round=Button("应用圆角",24,295,205); round.Click+=(s,e)=>Run(()=>{item.ApplyRounded(clean.Checked); return "已应用圆角，原图标可随时恢复。";});
        var launcher=Button("生成无箭头 EXE…",250,295,214);
        launcher.Click+=(s,e)=>
        {
            using(var save=new SaveFileDialog{Title="保存无箭头启动器",Filter="应用程序 (*.exe)|*.exe",DefaultExt="exe",AddExtension=true,
                InitialDirectory=Path.GetDirectoryName(item.LinkPath),FileName=Path.GetFileNameWithoutExtension(item.LinkPath)+"（无箭头）.exe",OverwritePrompt=false})
            {
                if(save.ShowDialog(this)!=DialogResult.OK) return;
                Run(()=>{using(var icon=item.ReadIcon(false)) item.CreateLauncher(save.FileName,icon); return "已生成启动器，原快捷方式已保留。";});
            }
        };
        AddLabel("无箭头 EXE 保留当前图标；如需圆角，请先应用圆角。\n原快捷方式保留，不改变其他图标的箭头。",25,346,440,43,9,false);
        restore=Button("恢复原图标",24,397,135); restore.Height=30;
        restore.Click+=(s,e)=>Run(()=>{item.Restore();return "已恢复原图标。";});
        feedback=AddLabel("",172,400,295,37,9,false);
        RefreshImages(); ScaleForDpi();
    }
    Label AddLabel(string text,int x,int y,int w,int h,float size,bool bold)
    {var label=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),AutoEllipsis=true,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Color.FromArgb(54,73,65)};Controls.Add(label);return label;}
    Button Button(string text,int x,int y,int width)
    {var b=new Button{Text=text,Bounds=new Rectangle(x,y,width,40),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(185,202,194);Controls.Add(b);return b;}
    void Run(Func<string> action)
    {try{feedback.Text=action();RefreshImages();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    void RefreshImages()
    {
        using(var source=item.ReadIcon(true))
        {if(originalView.Image!=null)originalView.Image.Dispose();originalView.Image=new Bitmap(source);if(roundedView.Image!=null)roundedView.Image.Dispose();roundedView.Image=IndividualIcon.Rounded(source,clean.Checked);}
        if(restore!=null)restore.Enabled=item.CanRestore;
    }
    void ScaleForDpi()
    {using(var g=CreateGraphics()){float scale=g.DpiX/96F;foreach(Control c in Controls)c.Bounds=new Rectangle((int)(c.Left*scale),(int)(c.Top*scale),(int)(c.Width*scale),(int)(c.Height*scale));ClientSize=new Size((int)(488*scale),(int)(448*scale));}}
    protected override void Dispose(bool disposing)
    {if(disposing){if(originalView!=null&&originalView.Image!=null)originalView.Image.Dispose();if(roundedView!=null&&roundedView.Image!=null)roundedView.Image.Dispose();}base.Dispose(disposing);}
}
