// Markdown Studio - desktop launcher
//
// Serves the bundled index.html from a loopback socket and opens it in Edge (or
// Chrome) as an app window. Going through http:// rather than file:// is the whole
// point: browsers only grant the File System Access API on an http(s) origin, so
// this is what makes "Open Folder" and "save straight back to disk" work without
// needing laragon running.
//
// When a file is passed on the command line (right-click a .md -> Open with ->
// Markdown Studio) the containing folder becomes a workspace this process serves,
// so the app opens that file immediately and can still browse and save its
// siblings. The browser cannot reach a path on its own, so those reads and writes
// go through /__ws and /__file here, and /__all hands "Search in folder" every
// file at once. /__asset reads and writes the pictures a document links to, so a
// screenshot pasted into the editor lands as a file beside it.
//
// The port is fixed, because the browser keys everything it stores (the Docs,
// settings, the remembered folder) to the origin, and the origin includes the
// port - a random port per launch quietly started every session empty. Only one
// copy can own that port, so a second launch hands its files to the running copy
// and just opens another window on it.
//
// One folder is always served, whatever the launch was: the library. That is
// where documents written inside the app live, so they are ordinary .md files on
// disk rather than rows in the browser's storage - they survive a reinstall,
// Windows Search can read them, and Explorer can open them like any other file.
// The page reaches that folder as w=lib.
//
// Where the library sits is the user's to choose. Until they do it is
// Documents\Markdown Studio, resolved per machine and per user; picking another
// folder moves the notes there and writes the choice next to the instance file, so
// every later launch on this machine finds it again.
//
// Every launch also registers the app for "Open with" on Markdown files, puts a
// shortcut in the Start menu so typing its name finds it, and asks the indexer to
// read inside .md files. All in the current user's registry and profile only.
// --register does just that, --unregister undoes it.
//
// The exe is the whole program - index.html is baked into it - and it is its own
// installer. A copy that is not the installed one shows setup and nothing else:
// install, or nothing happens. That is what stops the program living in whatever
// folder it was downloaded to, where the Start menu entry and the file associations
// it registers would break the moment that folder is tidied away.
//
// Setup asks where to put it - LocalAppData\Programs by default, but anywhere the
// account can write - and remembers the answer in the Apps & features entry, which is
// where Windows keeps that anyway. Wherever it lands is what "Open with", the Start
// menu and the uninstaller then point at. --install and --uninstall do it without
// asking. --portable runs from where it stands without installing: that is for
// working on the app itself, where the point is to run the copy you just built.
//
// Build:  build-exe.cmd

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Management;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Markdown Studio")]
[assembly: AssemblyProduct("Markdown Studio")]
[assembly: AssemblyDescription("Offline Markdown editor with live preview")]
[assembly: AssemblyVersion("1.16.0.0")]
[assembly: AssemblyFileVersion("1.16.0.0")]

static class MarkdownStudio
{
    // The one port every launch tries first. Below 49152, so it is outside the
    // range Windows hands out for outgoing connections.
    const int PreferredPort = 41873;

    static byte[] page;
    static string key = "";               // guards the workspace endpoints

    // One per window that was opened on a file or folder; the page names its own
    // with &w= in the URL, so several windows can share this one server.
    class Workspace
    {
        public string Root;               // full path of the folder we serve
        public string Name = "";          // its display name
        public List<string> Open = new List<string>();   // files to open on start
    }
    static readonly Dictionary<string, Workspace> workspaces = new Dictionary<string, Workspace>();

    // The workspace id the page uses for the library. Fixed, because every window
    // needs it and none of them was launched on it.
    const string LibId = "lib";

    const int MaxDepth = 6;
    const int MaxFiles = 3000;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            bool portable = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--register") { Register(); return 0; }
                if (a == "--unregister") { Unregister(); return 0; }
                if (a == "--install")
                {
                    // "--install D:\Apps\Markdown Studio" puts it there without the window,
                    // which is how it can be rolled out without anyone sitting at the screen
                    string where = i + 1 < args.Length && !args[i + 1].StartsWith("--")
                                 ? args[i + 1] : null;
                    string bad = Install(args, false, where);
                    if (bad != null)
                        MessageBox.Show(bad, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return bad == null ? 0 : 1;
                }
                if (a == "--uninstall") { Uninstall(true); return 0; }
                if (a == "--portable") portable = true;
            }
            if (!portable && Gate(args)) return 0;   // not installed, so nothing runs
            Sweep();
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Markdown Studio could not start.\n\n" + ex.Message,
                "Markdown Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // Installing over a copy whose window is still open has to leave that file behind
    // under another name, because Windows will not delete a running program. The next
    // launch is the first moment it can go, and one that is still in use simply
    // refuses and waits for the launch after that.
    static void Sweep()
    {
        try
        {
            if (!IsInstalledCopy()) return;
            foreach (string old in Directory.GetFiles(InstallDir(), "*.old-*"))
            {
                try { File.Delete(old); } catch { }
            }
        }
        catch { }
    }

    static void Run(string[] args)
    {
        page = LoadPage();
        try { Register(); } catch { }      // never worth failing a launch over
        // and the Installed apps entry follows the copy that is running, version and size
        try { if (IsInstalledCopy()) ListInApps(Assembly.GetExecutingAssembly().Location, InstallDir()); } catch { }
        Workspace ws = TakeWorkspace(args);

        string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownStudio", "profile");
        ProfileDir = profile;

        // already running: that copy serves this window too, so its origin (and
        // everything stored under it) is the same one you had open before
        string handed = HandOff(ws);
        if (handed != null)
        {
            OpenWindow(handed, profile, false);
            return;
        }

        key = NewKey();
        lock (workspaces) workspaces[LibId] = NewWorkspace(LibraryDir());
        string id = AddWorkspace(ws);
        TcpListener listener = Listen();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ServePort = port;

        Thread server = new Thread(delegate() { Serve(listener); });
        server.IsBackground = true;
        server.Start();

        WriteInstance(port);
        try
        {
            OpenWindow(PageUrl(port, id), profile, true);
        }
        finally
        {
            ClearInstance();
        }
    }

    // The folder the user picked, if they ever did. Kept beside the instance file,
    // in this machine's own profile - the path is a property of the machine, not of
    // the exe, so a copy carried on a USB stick still finds each machine's own.
    static string LibraryPathFile()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownStudio", "library");
    }

    // Until something is chosen: Documents, because Windows Search indexes it out of
    // the box, so a note saved there turns up in the search box with nothing else set
    // up. Resolved by asking Windows, so it is this machine's Documents for this user
    // - redirected to OneDrive, moved to another drive or renamed, it still lands right.
    static string DefaultLibraryDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Markdown Studio");
    }

    static string LibraryDir()
    {
        string chosen = null;
        try { chosen = File.ReadAllText(LibraryPathFile()).Trim(); } catch { }
        if (chosen != null && chosen.Length > 0)
        {
            // A folder on a drive that is not plugged in today falls back for this run
            // only. The choice stays on file, so it is picked up again once the drive
            // is back rather than silently becoming a new empty library somewhere else.
            try { Directory.CreateDirectory(chosen); return Path.GetFullPath(chosen); }
            catch { }
        }
        string dir = DefaultLibraryDir();
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    static void SaveLibraryDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPathFile()));
            // back to the default means forgetting the choice, not writing it down
            if (string.Equals(dir, DefaultLibraryDir(), StringComparison.OrdinalIgnoreCase))
                File.Delete(LibraryPathFile());
            else
                File.WriteAllText(LibraryPathFile(), dir);
        }
        catch { }
    }

    // ---------------------------------------------------------------- folder picker

    // Windows' own folder picker is the File Explorer window with everything but
    // folders switched off: an address bar you can paste a path into, search, Quick
    // access, resizable. WinForms cannot reach it on .NET Framework - its
    // FolderBrowserDialog is still the little "Browse For Folder" tree from XP - so
    // the shell interfaces are declared here by hand. The method order below IS the
    // vtable: nothing may be reordered, renamed away or left out.

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog { }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    const uint FOS_PICKFOLDERS = 0x00000020;
    const uint FOS_FORCEFILESYSTEM = 0x00000040;
    const uint FOS_PATHMUSTEXIST = 0x00000800;
    const uint SIGDN_FILESYSPATH = 0x80058000;
    const int ERROR_CANCELLED = unchecked((int)0x800704C7);

    // Null when the dialog was cancelled. Anything else that goes wrong throws, so
    // the caller can fall back rather than read a failure as "no thanks".
    static string PickModern(string start, IntPtr owner, string title)
    {
        IFileDialog dlg = (IFileDialog)(new FileOpenDialog());
        try
        {
            uint opts;
            dlg.GetOptions(out opts);
            dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            dlg.SetTitle(title);
            dlg.SetOkButtonLabel("Use this folder");

            if (!string.IsNullOrEmpty(start))
            {
                // a start folder that has gone is not worth failing the dialog over
                try
                {
                    Guid iid = typeof(IShellItem).GUID;
                    object si;
                    SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out si);
                    dlg.SetFolder((IShellItem)si);
                }
                catch { }
            }

            int hr = dlg.Show(owner);
            if (hr == ERROR_CANCELLED) return null;
            if (hr != 0) throw new COMException("folder dialog failed", hr);

            IShellItem res;
            dlg.GetResult(out res);
            IntPtr name;
            res.GetDisplayName(SIGDN_FILESYSPATH, out name);
            try { return Marshal.PtrToStringUni(name); }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { try { Marshal.ReleaseComObject(dlg); } catch { } }
    }

    // The old tree dialog, for a Windows where the shell interfaces above are not
    // there. Cramped, but a picker that works beats a button that does nothing.
    static string PickClassic(string start, IWin32Window owner, string title)
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = title;
            d.ShowNewFolderButton = true;
            d.RootFolder = Environment.SpecialFolder.MyComputer;
            try { d.SelectedPath = start; } catch { }
            return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
        }
    }

    // The modern picker, falling back to the tree. Call it from a thread that is
    // already STA and owns a window - setup does; the library endpoint does not.
    static string FolderDialog(string start, IWin32Window owner, IntPtr handle, string title)
    {
        try { return PickModern(start, handle, title); }
        catch { return PickClassic(start, owner, title); }
    }

    // Asks for a folder on a thread of its own: the file dialogs need an STA thread,
    // and the one serving this request is a pool thread. Returns null if cancelled.
    static string AskFolder(string start)
    {
        string picked = null;
        Thread t = new Thread(delegate()
        {
            // Our process has no window of its own - the app window belongs to the
            // browser - so without an owner on top the dialog can open behind it.
            using (Form owner = new Form())
            {
                owner.FormBorderStyle = FormBorderStyle.None;
                owner.ShowInTaskbar = false;
                owner.Opacity = 0;
                owner.Size = new System.Drawing.Size(1, 1);
                owner.StartPosition = FormStartPosition.CenterScreen;
                owner.TopMost = true;
                owner.Show();
                owner.Activate();
                picked = FolderDialog(start, owner, owner.Handle, LIB_TITLE);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        return picked;
    }

    // A free name beside the ones already in the folder, for a note whose name is
    // taken there. Null when even that fails, and the file is then left where it is.
    static string Free(string root, string rel)
    {
        string win = rel.Replace('/', '\\');
        string dir = Path.GetDirectoryName(win);
        string name = Path.GetFileNameWithoutExtension(win);
        string ext = Path.GetExtension(win);
        for (int n = 2; n < 1000; n++)
        {
            string cand = (dir.Length > 0 ? dir + "\\" : "") + name + " " + n + ext;
            if (!File.Exists(Path.Combine(root, cand))) return cand.Replace('\\', '/');
        }
        return null;
    }

    // Moves the library's notes to a new folder. A name already taken there is not
    // overwritten - that note arrives under a free name instead, and the page is told
    // so it can follow. Anything already in the new folder simply joins the library.
    static string MoveLibrary(string from, string to, out int moved, out int kept)
    {
        moved = 0; kept = 0;
        StringBuilder renamed = new StringBuilder("{");
        bool first = true;
        // the assets go too, or every note that shows a picture would lose it
        List<string> files = new List<string>();
        Walk(from, "", 0, files, delegate(string f) { return IsMd(f) || InAssets(f); });
        foreach (string rel in files)
        {
            string src, dstRel = rel;
            try { src = Path.GetFullPath(Path.Combine(from, rel.Replace('/', '\\'))); }
            catch { kept++; continue; }
            string dst;
            try { dst = Path.GetFullPath(Path.Combine(to, rel.Replace('/', '\\'))); }
            catch { kept++; continue; }
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(dst))
            {
                dstRel = Free(to, rel);
                if (dstRel == null) { kept++; continue; }
                dst = Path.Combine(to, dstRel.Replace('/', '\\'));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Move(src, dst);
                moved++;
                if (dstRel != rel)
                {
                    if (!first) renamed.Append(',');
                    first = false;
                    renamed.Append(JsonStr(rel)).Append(':').Append(JsonStr(dstRel));
                }
            }
            catch { kept++; }
        }
        return renamed.Append('}').ToString();
    }

    // Answers the page's "let me choose where my documents live".
    static string ChooseLibrary()
    {
        string cur;
        lock (workspaces) cur = workspaces[LibId].Root;

        string picked = AskFolder(cur);
        if (picked == null) return "{\"cancelled\":true}";

        string full;
        try { full = Path.GetFullPath(picked); }
        catch { return "{\"error\":\"that path cannot be used\"}"; }

        int moved = 0, kept = 0;
        string renamed = "{}";
        if (!string.Equals(full, cur, StringComparison.OrdinalIgnoreCase))
        {
            try { Directory.CreateDirectory(full); }
            catch (Exception ex) { return "{\"error\":" + JsonStr(ex.Message) + "}"; }
            renamed = MoveLibrary(cur, full, out moved, out kept);
            SaveLibraryDir(full);
            lock (workspaces) workspaces[LibId] = NewWorkspace(full);
        }

        Workspace ws;
        lock (workspaces) ws = workspaces[LibId];
        return "{\"path\":" + JsonStr(ws.Root) + ",\"root\":" + JsonStr(ws.Name) +
               ",\"moved\":" + moved + ",\"kept\":" + kept +
               ",\"renamed\":" + renamed + "}";
    }

    static string PageUrl(int port, string id)
    {
        return "http://127.0.0.1:" + port + "/?k=" + key + (id != null ? "&w=" + id : "");
    }

    static TcpListener Listen()
    {
        try
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, PreferredPort);
            l.ExclusiveAddressUse = true;     // nobody else may share the port with us
            l.Start();
            return l;
        }
        catch (SocketException)
        {
            // something else holds it - still start, just without the stable origin
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return l;
        }
    }

    // Opens the app window. The first copy waits for it to close so the server
    // stays up; a copy that only handed off returns straight away.
    static void OpenWindow(string url, string profile, bool wait)
    {
        string browser = FindBrowser();
        if (browser == null)
        {
            // no Chromium browser found - fall back to whatever handles http
            Process.Start(url);
            if (wait)
                MessageBox.Show("Markdown Studio is running at\n" + url +
                    "\n\nClose this dialog when you are done.",
                    "Markdown Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // its own profile folder, so the app window is independent of your normal
        // browsing session and keeps its own documents and settings
        bool firstRun = !Directory.Exists(profile);
        Directory.CreateDirectory(profile);

        ProcessStartInfo psi = new ProcessStartInfo(browser);
        psi.Arguments =
            "--app=" + url +
            " --user-data-dir=\"" + profile + "\"" +
            WindowArgs(firstRun) +
            " --no-first-run --no-default-browser-check --disable-background-mode";
        psi.UseShellExecute = false;

        Process proc = Process.Start(psi);
        if (proc == null || !wait) return;

        // With its own --user-data-dir the process we started IS the browser, so this
        // blocks until the window is closed. If it returns straight away the browser
        // handed off to another process, so fall back to watching for that one.
        proc.WaitForExit();
        if (DateTime.Now - proc.StartTime < TimeSpan.FromSeconds(3))
            WaitForOurWindow(profile);
    }

    // Sized to the screen rather than a fixed 1360x900, which is taller than a
    // 768px laptop can show. Only on the first run: after that the profile
    // remembers whatever size the window was left at, and forcing one would
    // undo the user's own resizing on every launch.
    static string WindowArgs(bool firstRun)
    {
        if (!firstRun) return "";
        try
        {
            System.Drawing.Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(1440, Math.Max(360, wa.Width - 60));
            int h = Math.Min(980, Math.Max(400, wa.Height - 60));
            int x = wa.Left + (wa.Width - w) / 2;
            int y = wa.Top + (wa.Height - h) / 2;
            return " --window-size=" + w + "," + h + " --window-position=" + x + "," + y;
        }
        catch { return " --window-size=1280,860"; }
    }

    static string NewKey()
    {
        byte[] b = new byte[16];
        using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b);
        return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
    }

    // ---------------------------------------------------------------- single instance

    // Where the running copy leaves its port and key for the next launch to find.
    // It sits in your own profile, which is the same trust boundary as the key in
    // the window's URL.
    static string InstanceFile()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownStudio", "instance");
    }

    static void WriteInstance(int port)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InstanceFile()));
            File.WriteAllText(InstanceFile(),
                port + "\n" + key + "\n" + Process.GetCurrentProcess().Id + "\n");
        }
        catch { }
    }

    static void ClearInstance()
    {
        try
        {
            string[] l = File.ReadAllLines(InstanceFile());
            if (l.Length >= 3 && l[2] == Process.GetCurrentProcess().Id.ToString())
                File.Delete(InstanceFile());
        }
        catch { }
    }

    // Asks a running copy to take this launch's files. Returns the URL of the new
    // window, or null when there is no running copy (or it did not answer), in
    // which case this launch simply becomes the server itself.
    static string HandOff(Workspace ws)
    {
        string[] l;
        try { l = File.ReadAllLines(InstanceFile()); } catch { return null; }
        if (l.Length < 3) return null;

        int port, pid;
        if (!int.TryParse(l[0], out port) || !int.TryParse(l[2], out pid)) return null;
        try
        {
            // a leftover from a copy that did not shut down cleanly
            Process p = Process.GetProcessById(pid);
            if (p.HasExited || p.Id == Process.GetCurrentProcess().Id) return null;
        }
        catch { return null; }

        StringBuilder body = new StringBuilder();
        if (ws != null)
        {
            body.Append(ws.Root).Append('\n');
            foreach (string f in ws.Open) body.Append(f).Append('\n');
        }

        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(
                "http://127.0.0.1:" + port + "/__launch?k=" + l[1]);
            rq.Method = "POST";
            rq.Proxy = null;
            rq.Timeout = 3000;
            rq.ContentType = "text/plain; charset=utf-8";
            byte[] b = Encoding.UTF8.GetBytes(body.ToString());
            rq.ContentLength = b.Length;
            using (Stream s = rq.GetRequestStream()) s.Write(b, 0, b.Length);
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader rd = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
            {
                string id = rd.ReadToEnd().Trim();
                return "http://127.0.0.1:" + port + "/?k=" + l[1] + (id.Length > 0 ? "&w=" + id : "");
            }
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- installing

    const string AppName = "Markdown Studio";
    const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MarkdownStudio";

    // Per user, so no administrator is needed and nothing is put where another
    // account could be surprised by it. This is where Windows itself puts the
    // programs a user installs for themselves, so it is what setup suggests.
    static string DefaultInstallDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
    }

    // Where it actually went. Read back from the Apps & features entry rather than
    // kept somewhere of our own: that is where Windows records an install location,
    // it is written at install time anyway, and it disappears with the uninstall.
    static string InstallDir()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                if (k != null)
                {
                    object v = k.GetValue("InstallLocation");
                    if (v != null && v.ToString().Trim().Length > 0) return v.ToString();
                }
            }
        }
        catch { }
        return DefaultInstallDir();
    }
    static string ExeIn(string dir) { return Path.Combine(dir, "MarkdownStudio.exe"); }
    static string InstalledExe() { return ExeIn(InstallDir()); }

    static bool IsInstalledCopy()
    {
        try
        {
            return string.Equals(Assembly.GetExecutingAssembly().Location, InstalledExe(),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // The gate. A copy that is not the installed one does not open the editor: it runs
    // setup, and the editor opens from the copy setup puts in place. Returns true when
    // this process is finished - either because it was cancelled, or because the
    // installed copy has taken over. A folder that will not take it sends the user
    // back to setup rather than quietly starting the app from the wrong place.
    static bool Gate(string[] args)
    {
        try
        {
            if (IsInstalledCopy()) return false;
            string dir = InstallDir();
            for (;;)
            {
                dir = Setup(dir);
                if (dir == null) return true;             // cancelled, so nothing opens
                string bad = Install(args, true, dir);
                if (bad == null) return true;
                MessageBox.Show(bad, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch { return false; }
    }

    // A window rather than a message box: this is the first thing anyone sees of the
    // program, and where it is about to put itself is not just worth showing - it is
    // worth being able to change. Returns the folder, or null if cancelled.
    static string Setup(string suggest)
    {
        using (Form f = new Form())
        {
            f.Text = "Set up " + AppName;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = true;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new System.Drawing.Size(478, 254);
            f.BackColor = System.Drawing.Color.White;
            f.Font = new System.Drawing.Font("Segoe UI", 9F);
            try { f.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                      Assembly.GetExecutingAssembly().Location); }
            catch { }

            Label name = new Label();
            name.Text = AppName;
            name.Font = new System.Drawing.Font("Segoe UI", 16F);
            name.SetBounds(26, 22, 430, 32);
            Label what = new Label();
            what.Text = "Offline Markdown editor with live preview";
            what.ForeColor = System.Drawing.Color.FromArgb(105, 112, 122);
            what.SetBounds(28, 56, 430, 20);

            Label lead = new Label();
            lead.Text = "It will be installed for your account only - no administrator, " +
                        "and nothing written outside your own profile.";
            lead.SetBounds(26, 92, 428, 36);

            Label at = new Label();
            at.Text = "Install to";
            at.ForeColor = System.Drawing.Color.FromArgb(105, 112, 122);
            at.SetBounds(26, 132, 430, 18);

            TextBox path = new TextBox();
            path.Text = string.IsNullOrEmpty(suggest) ? DefaultInstallDir() : suggest;
            path.BorderStyle = BorderStyle.FixedSingle;
            path.SetBounds(26, 152, 336, 24);

            Button browse = new Button();
            browse.Text = "Browse\u2026";
            browse.SetBounds(370, 151, 82, 27);
            browse.Click += delegate
            {
                string start = path.Text.Trim();
                try { while (start.Length > 3 && !Directory.Exists(start))
                          start = Path.GetDirectoryName(start); }
                catch { start = DefaultInstallDir(); }
                string picked = FolderDialog(start, f, f.Handle, SETUP_TITLE);
                if (picked == null) return;
                // a folder of their own things should not have the exe dropped into it,
                // and picking the app's own folder again should not nest another one
                if (!string.Equals(Path.GetFileName(picked.TrimEnd('\\')), AppName,
                                   StringComparison.OrdinalIgnoreCase))
                    picked = Path.Combine(picked, AppName);
                path.Text = picked;
            };

            Label note = new Label();
            note.Text = "Wherever it goes, \"Open with\" and the Start menu follow it. Your " +
                        "documents are not touched, and it can be removed later from " +
                        "Settings \u203a Apps.";
            note.ForeColor = System.Drawing.Color.FromArgb(105, 112, 122);
            note.SetBounds(26, 186, 428, 38);

            Button ok = new Button();
            ok.Text = "Install";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(248, 234, 100, 30);
            Button no = new Button();
            no.Text = "Cancel";
            no.DialogResult = DialogResult.Cancel;
            no.SetBounds(356, 234, 96, 30);

            f.ClientSize = new System.Drawing.Size(478, 278);
            f.Controls.AddRange(new Control[] { name, what, lead, at, path, browse, note, ok, no });
            f.AcceptButton = ok;
            f.CancelButton = no;
            f.Shown += delegate { f.Activate(); ok.Focus(); };
            if (f.ShowDialog() != DialogResult.OK) return null;
            return path.Text.Trim().Trim('"');
        }
    }

    // Windows will not delete a file that is running, but it will rename one - which
    // is how an installed copy can be replaced while its window is still open.
    static void PlaceExe(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        foreach (string old in Directory.GetFiles(Path.GetDirectoryName(to), "*.old-*"))
        {
            try { File.Delete(old); } catch { }
        }
        if (File.Exists(to))
        {
            try { File.Delete(to); }
            catch
            {
                File.Move(to, to + ".old-" + NewKey().Substring(0, 8));
            }
        }
        File.Copy(from, to, true);
    }

    // Null when it worked, otherwise why it did not - so setup can say so and ask again
    // instead of starting the program from a folder the user did not choose.
    static string Install(string[] args, bool launch, string dir)
    {
        if (string.IsNullOrEmpty(dir)) dir = InstallDir();
        try { dir = Path.GetFullPath(dir); }
        catch { return "That does not look like a folder path."; }
        if (!Path.IsPathRooted(dir))
            return "Please give a full path, starting with a drive letter.";

        string here = Assembly.GetExecutingAssembly().Location;
        string exe = ExeIn(dir);
        if (string.Equals(here, exe, StringComparison.OrdinalIgnoreCase))
        {
            Register();                       // already in place, just make sure it is known
            try { ListInApps(exe, dir); } catch { }
            return null;
        }
        try
        {
            PlaceExe(here, exe);
        }
        catch (UnauthorizedAccessException)
        {
            return "Markdown Studio is not allowed to write to\n\n    " + dir +
                   "\n\nThat folder needs administrator rights. Pick somewhere inside " +
                   "your own account instead - the suggested location always works.";
        }
        catch (Exception ex)
        {
            return "Markdown Studio could not install itself to\n\n    " + dir +
                   "\n\n" + ex.Message;
        }

        // the associations and the Start menu entry must point at the copy that stays,
        // wherever the user put it
        try { Register(exe); } catch { }
        try { ListInApps(exe, dir); } catch { }

        if (!launch) return null;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.Arguments = Quote(args);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = dir;
            Process.Start(psi);
        }
        catch
        {
            MessageBox.Show("Markdown Studio is installed. Start it from the Start menu.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return null;
    }

    static string Quote(string[] args)
    {
        StringBuilder b = new StringBuilder();
        foreach (string a in args)
        {
            if (a == "--install" || a == "--portable") continue;
            if (b.Length > 0) b.Append(' ');
            b.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
        }
        return b.ToString();
    }

    // The entry in Settings > Apps & features. Current user only, like everything else.
    // InstallLocation is written here first and read back by InstallDir(), so this is
    // also the record of where the user chose to put it.
    static void ListInApps(string exe, string dir)
    {
        long size = 0;
        try { size = new FileInfo(exe).Length / 1024; } catch { }
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            // from the exe itself, so the entry can never say an older version than the
            // program it points at
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            k.SetValue("DisplayVersion", v.Major + "." + v.Minor + "." + v.Build);
            k.SetValue("Publisher", AppName);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
            k.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    // A program cannot delete the file it is running from, so the folder goes a few
    // seconds later, once this process has gone.
    static void RemoveLater(string dir)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 4 >nul & rd /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch { }
    }

    static void Uninstall(bool ask)
    {
        if (ask)
        {
            bool quiet = false;
            foreach (string a in Environment.GetCommandLineArgs()) if (a == "--quiet") quiet = true;
            if (!quiet && MessageBox.Show(
                    "Remove Markdown Studio from this account?\n\n" +
                    "Your documents stay where they are, and so do your settings - only the " +
                    "program itself is removed.",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
        }
        // where it went is recorded in the key that is about to be deleted
        string dir = InstallDir();
        try { Unregister(); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        // Only the program goes. Everything under LocalAppData\MarkdownStudio is the
        // user's - the settings and open documents in the browser profile, and which
        // folder they chose for their library - and a window may be reading it this
        // very moment. Installing again picks up where they left off.
        if (Directory.Exists(dir)) RemoveLater(dir);
    }

    // ---------------------------------------------------------------- "Open with"

    static readonly string[] MdExts = { ".md", ".markdown", ".mdown", ".mkd", ".mdwn" };
    const string ProgId = "MarkdownStudio.md";

    static bool Put(RegistryKey root, string sub, string name, string value)
    {
        using (RegistryKey k = root.CreateSubKey(sub))
        {
            object cur = k.GetValue(name);
            if (cur != null && cur.ToString() == value) return false;
            k.SetValue(name, value);
            return true;
        }
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);

    // Windows only remembers an app picked through "Choose another app" in a short
    // recently-used list, and drops it again once other apps push it out - which is
    // why Markdown Studio kept vanishing from the menu. Listing it under each
    // extension's OpenWithProgids keeps it there for good. Current user only, so
    // no admin rights; it does not make itself the default app.
    // The plain-text reader Windows Search already ships with. Pointed at .md it
    // indexes what is inside the file, not just its name - which is what makes a
    // phrase from a note findable from the Windows search box.
    const string TextHandler = "{5e941d80-bf96-11cd-b579-08002b30bfeb}";

    const string LIB_TITLE = "Where should Markdown Studio keep your documents?";
    const string SETUP_TITLE = "Where should Markdown Studio be installed?";

    static void Register() { Register(Assembly.GetExecutingAssembly().Location); }

    // `exe` is which copy the menu entries should point at - after installing that is
    // the installed one, not the copy the user happened to double-click.
    static void Register(string exe)
    {
        string file = Path.GetFileName(exe);
        // a copy the build moved aside must not steal the registration
        if (file.StartsWith("MarkdownStudio.old", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("MarkdownStudio.new", StringComparison.OrdinalIgnoreCase)) return;

        string cmd = "\"" + exe + "\" \"%1\"";
        string icon = "\"" + exe + "\",0";
        string app = @"Applications\" + file;
        bool changed = false;
        using (RegistryKey c = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
        {
            changed |= Put(c, ProgId, "", "Markdown document");
            changed |= Put(c, ProgId, "FriendlyTypeName", "Markdown document");
            changed |= Put(c, ProgId + @"\DefaultIcon", "", icon);
            changed |= Put(c, ProgId + @"\shell\open\command", "", cmd);
            changed |= Put(c, app, "FriendlyAppName", "Markdown Studio");
            changed |= Put(c, app + @"\DefaultIcon", "", icon);
            changed |= Put(c, app + @"\shell\open\command", "", cmd);
            foreach (string e in MdExts)
            {
                changed |= Put(c, e + @"\OpenWithProgids", ProgId, "");
                changed |= Put(c, app + @"\SupportedTypes", e, "");
                // Named values and a subkey only. The extension key's own default
                // value is what names the default app, and that stays untouched.
                changed |= Put(c, e + @"\PersistentHandler", "", TextHandler);
                changed |= Put(c, e, "PerceivedType", "text");
                changed |= Put(c, e, "Content Type", "text/markdown");
            }
        }
        Shortcut(exe);
        // tell Explorer, or the menu keeps showing the old state until a restart
        if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    static string ShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "Markdown Studio.lnk");
    }

    // A Start menu entry, which is the only thing that puts the app in the Windows
    // search box - the "Open with" registration alone never does. Written through
    // WScript.Shell by name, so nothing has to be referenced at build time.
    static void Shortcut(string exe)
    {
        try
        {
            string path = ShortcutPath();
            Type wsh = Type.GetTypeFromProgID("WScript.Shell");
            if (wsh == null) return;
            object shell = Activator.CreateInstance(wsh);
            object lnk = wsh.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { path });
            Type lt = lnk.GetType();

            // a shortcut that already points here needs no write at all
            if (File.Exists(path))
            {
                object cur = lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                if (cur != null && string.Equals(cur.ToString(), exe, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk,
                new object[] { Path.GetDirectoryName(exe) });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk,
                new object[] { exe + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk,
                new object[] { "Offline Markdown editor with live preview" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
        catch { }   // no shortcut is a smaller problem than a launch that fails
    }

    static void Unregister()
    {
        string file = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        List<string> ours = new List<string>();
        using (RegistryKey c = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
        {
            c.DeleteSubKeyTree(ProgId, false);
            c.DeleteSubKeyTree(@"Applications\" + file, false);
            foreach (string e in MdExts)
            {
                using (RegistryKey k = c.OpenSubKey(e + @"\OpenWithProgids", true))
                    if (k != null) k.DeleteValue(ProgId, false);
                // only the handler we set goes; one another program registered stays
                using (RegistryKey k = c.OpenSubKey(e + @"\PersistentHandler", false))
                {
                    object v = k == null ? null : k.GetValue("");
                    if (v != null && v.ToString() == TextHandler) ours.Add(e);
                }
            }
            foreach (string e in ours) c.DeleteSubKeyTree(e + @"\PersistentHandler", false);
        }
        try { File.Delete(ShortcutPath()); } catch { }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    // ---------------------------------------------------------------- workspace

    static bool IsMd(string path)
    {
        string e = Path.GetExtension(path).ToLowerInvariant();
        return e == ".md" || e == ".markdown" || e == ".mdown" || e == ".mkd" ||
               e == ".mdwn" || e == ".mdx" || e == ".txt";
    }

    // Pictures a document can show. Only these go through /__asset, so it cannot be
    // used to read or plant anything else in the folder.
    static string ImageType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": return "image/png";
            case ".jpg": case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".webp": return "image/webp";
            case ".bmp": return "image/bmp";
            case ".svg": return "image/svg+xml";
            case ".avif": return "image/avif";
            case ".ico": return "image/x-icon";
        }
        return null;
    }

    static bool IsImage(string path) { return ImageType(path) != null; }

    // Where pasted pictures go: an "assets" folder beside the document. Anything in
    // one, at any depth, belongs to the documents next to it.
    const string AssetDir = "assets";

    static bool InAssets(string path)
    {
        string[] segs = path.Replace('/', '\\').Split('\\');
        for (int i = 0; i < segs.Length - 1; i++)
            if (string.Equals(segs[i], AssetDir, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool SkipDir(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "node_modules": case ".git": case ".svn": case ".hg": case "vendor":
            case "dist": case "build": case "out": case "target": case ".next":
            case ".nuxt": case ".cache": case "__pycache__": case ".venv": case "venv":
                return true;
        }
        return false;
    }

    // Everything the command line gave us. A folder becomes the workspace as it
    // is; files make their own folder the workspace so links between siblings
    // still resolve. Null when launched with nothing to open.
    static Workspace TakeWorkspace(string[] args)
    {
        List<string> files = new List<string>();
        string dir = null;

        foreach (string a in args)
        {
            if (string.IsNullOrEmpty(a) || a.StartsWith("-")) continue;
            string full;
            try { full = Path.GetFullPath(a); } catch { continue; }

            if (Directory.Exists(full)) { if (dir == null) dir = full; continue; }
            if (!File.Exists(full)) continue;
            if (dir == null) dir = Path.GetDirectoryName(full);
            files.Add(full);
        }
        if (dir == null) return null;

        Workspace ws = NewWorkspace(dir);
        foreach (string f in files)
        {
            string rel = RelOf(ws, f);
            // we took the first file's own folder as the root, so it always is
            if (rel != null) ws.Open.Add(rel);
        }
        return ws;
    }

    static Workspace NewWorkspace(string dir)
    {
        Workspace ws = new Workspace();
        // "D:\" must keep its backslash: "D:" alone means the current folder on D:
        string root = Path.GetFullPath(dir);
        ws.Root = root.Length > 3 ? root.TrimEnd('\\', '/') : root;
        ws.Name = Path.GetFileName(ws.Root);
        if (ws.Name.Length == 0) ws.Name = ws.Root;       // a drive root has no name
        return ws;
    }

    static string AddWorkspace(Workspace ws)
    {
        if (ws == null) return null;
        string id = NewKey().Substring(0, 12);
        lock (workspaces) workspaces[id] = ws;
        return id;
    }

    static Workspace GetWorkspace(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Workspace ws;
        lock (workspaces) return workspaces.TryGetValue(id, out ws) ? ws : null;
    }

    static string Prefix(Workspace ws)
    {
        return ws.Root.EndsWith("\\") ? ws.Root : ws.Root + "\\";
    }

    static string RelOf(Workspace ws, string full)
    {
        string r = Prefix(ws);
        if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
        return full.Substring(r.Length).Replace('\\', '/');
    }

    static void Walk(string dir, string prefix, int depth, List<string> into)
    {
        Walk(dir, prefix, depth, into, IsMd);
    }

    static void Walk(string dir, string prefix, int depth, List<string> into, Func<string, bool> keep)
    {
        if (depth > MaxDepth || into.Count >= MaxFiles) return;
        string[] entries;
        try { entries = Directory.GetFiles(dir); } catch { return; }
        foreach (string f in entries)
        {
            if (into.Count >= MaxFiles) return;
            // judged on the path inside the workspace, so where the workspace itself
            // sits on disk can never make a file count as something it is not
            string rel = prefix + Path.GetFileName(f);
            if (keep(rel)) into.Add(rel);
        }
        string[] subs;
        try { subs = Directory.GetDirectories(dir); } catch { return; }
        foreach (string s in subs)
        {
            string name = Path.GetFileName(s);
            if (name.StartsWith(".") || SkipDir(name)) continue;
            Walk(s, prefix + name + "/", depth + 1, into, keep);
        }
    }

    // Every folder, so the Explorer shows one with nothing in it yet - a folder just
    // made with New Folder, waiting for its first file - and not only the folders a
    // document happens to sit in. The same folders Walk goes into, no more.
    static void WalkDirs(string dir, string prefix, int depth, List<string> into)
    {
        if (depth >= MaxDepth || into.Count >= MaxFiles) return;
        string[] subs;
        try { subs = Directory.GetDirectories(dir); } catch { return; }
        foreach (string s in subs)
        {
            string name = Path.GetFileName(s);
            if (name.StartsWith(".") || SkipDir(name)) continue;
            into.Add(prefix + name);
            WalkDirs(s, prefix + name + "/", depth + 1, into);
        }
    }

    // A folder inside the workspace ("" is the workspace itself), for New Folder and
    // for moving a file into a folder. Held to what a file is held to: nothing that
    // climbs out, no drive letters, no hidden or skipped folders.
    static string ResolveDir(Workspace ws, string rel)
    {
        if (ws == null) return null;
        rel = (rel ?? "").Replace('\\', '/').Trim('/');
        if (rel.Length == 0) return ws.Root;
        if (rel.Contains(":")) return null;
        foreach (string seg in rel.Split('/'))
            if (seg.Length == 0 || seg == "." || seg == ".." || seg.StartsWith(".") || SkipDir(seg)) return null;
        string full;
        try { full = Path.GetFullPath(Path.Combine(ws.Root, rel.Replace('/', '\\'))); }
        catch { return null; }
        if (!full.StartsWith(Prefix(ws), StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    // Resolves a relative path from the page against the workspace, refusing
    // anything that escapes it or is not a text file we handle.
    static string Resolve(Workspace ws, string rel)
    {
        return Resolve(ws, rel, IsMd);
    }

    static string Resolve(Workspace ws, string rel, Func<string, bool> allowed)
    {
        if (ws == null || string.IsNullOrEmpty(rel)) return null;
        rel = rel.Replace('\\', '/');
        if (rel.StartsWith("/") || rel.Contains(":")) return null;
        // a ".." segment climbs out; "notes..md" is just a name and is fine
        foreach (string seg in rel.Split('/')) if (seg == "..") return null;
        if (!allowed(rel)) return null;

        string full;
        try { full = Path.GetFullPath(Path.Combine(ws.Root, rel.Replace('/', '\\'))); }
        catch { return null; }

        if (!full.StartsWith(Prefix(ws), StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    static string JsonStr(string s)
    {
        StringBuilder b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < ' ' || c > '~') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string Manifest(Workspace ws)
    {
        if (ws == null) return "{\"root\":null}";
        List<string> files = new List<string>();
        Walk(ws.Root, "", 0, files);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        StringBuilder b = new StringBuilder();
        b.Append("{\"root\":").Append(JsonStr(ws.Name));
        b.Append(",\"path\":").Append(JsonStr(ws.Root));
        b.Append(",\"files\":[");
        for (int i = 0; i < files.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(files[i]));
        }
        // Everything inside an assets folder, listed apart so the Explorer can show what
        // is in there without anything that reads documents mistaking it for text.
        // Outside one, only documents are listed - a folder full of other pictures is
        // not what the tree is for.
        List<string> pics = new List<string>();
        Walk(ws.Root, "", 0, pics, delegate(string f) { return InAssets(f) && !IsMd(f); });
        pics.Sort(StringComparer.OrdinalIgnoreCase);
        b.Append("],\"assets\":[");
        for (int i = 0; i < pics.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(pics[i]));
        }
        List<string> dirs = new List<string>();
        WalkDirs(ws.Root, "", 0, dirs);
        dirs.Sort(StringComparer.OrdinalIgnoreCase);
        b.Append("],\"dirs\":[");
        for (int i = 0; i < dirs.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(dirs[i]));
        }
        b.Append("],\"open\":[");
        for (int i = 0; i < ws.Open.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(ws.Open[i]));
        }
        b.Append("]}");
        return b.ToString();
    }

    // Every workspace file in one response, for "Search in folder". One request
    // per file is fine for a handful of notes but crawls once a folder has a few
    // hundred. Past the size cap the rest is left out, and the page reads those
    // one by one through /__file as before.
    const long MaxBulk = 48L * 1024 * 1024;

    static string AllFiles(Workspace ws)
    {
        StringBuilder b = new StringBuilder("{");
        if (ws == null) return b.Append('}').ToString();

        List<string> files = new List<string>();
        Walk(ws.Root, "", 0, files);
        long total = 0;
        bool first = true;
        foreach (string rel in files)
        {
            string full = Resolve(ws, rel);
            if (full == null) continue;
            byte[] raw;
            try { raw = File.ReadAllBytes(full); } catch { continue; }
            if (total + raw.Length > MaxBulk) break;
            total += raw.Length;
            if (!first) b.Append(',');
            first = false;
            b.Append(JsonStr(rel)).Append(':').Append(JsonStr(Encoding.UTF8.GetString(raw)));
        }
        return b.Append('}').ToString();
    }

    // A later launch's files, sent over by HandOff: the folder on the first line,
    // then the files to open relative to it. Answers with the new workspace id,
    // or nothing for a plain window with no file.
    static string Launch(byte[] body)
    {
        string[] lines = Encoding.UTF8.GetString(body).Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0].Trim().Length == 0) return "";
        if (!Directory.Exists(lines[0])) return "";

        Workspace ws = NewWorkspace(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            string rel = lines[i].Trim();
            if (rel.Length > 0 && Resolve(ws, rel) != null) ws.Open.Add(rel);
        }
        return AddWorkspace(ws);
    }

    // ---------------------------------------------------------------- lifecycle

    static void WaitForOurWindow(string profile)
    {
        // Only count browsers started with OUR profile. Counting every msedge would
        // keep this process alive for as long as the user's own browser is open.
        for (int i = 0; i < 40 && CountOurs(profile) == 0; i++) Thread.Sleep(500);
        while (CountOurs(profile) > 0) Thread.Sleep(1000);
    }

    // ---------------------------------------------------------------- picture-in-picture

    // Window plumbing for the floating window below: to Windows it is a window of its
    // own, so Alt+Tab and the taskbar would show Markdown Studio twice. It gets the
    // tool-window style floating palettes have - still on top and clickable, but no
    // second entry in Alt+Tab or on the taskbar.

    static string ProfileDir = "";

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int i, int v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    static long ExStyle(IntPtr h)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() : GetWindowLong32(h, GWL_EXSTYLE);
    }
    static void SetExStyle(IntPtr h, long v)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(v));
        else SetWindowLong32(h, GWL_EXSTYLE, (int)v);
    }

    // The taskbar keeps a button it has already made until it is told to drop it.
    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }
    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    class TaskbarList { }

    // Process ids of the browser running our profile - only its windows are ours to touch.
    static HashSet<uint> OurBrowserPids()
    {
        HashSet<uint> ids = new HashSet<uint>();
        if (ProfileDir.Length == 0) return ids;
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
            using (ManagementObjectCollection res = s.Get())
            {
                foreach (ManagementObject mo in res)
                {
                    object cl = mo["CommandLine"];
                    if (cl != null && cl.ToString().IndexOf(ProfileDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        ids.Add(Convert.ToUInt32(mo["ProcessId"]));
                }
            }
        }
        catch { }
        return ids;
    }

    // ---------------------------------------------------------------- the floating window

    // The browser's own picture-in-picture window prints the page's address in its title
    // bar - "127.0.0.1:41873" - and nothing a page does can change that. So from the exe
    // the floating window is one of our own app windows instead, the same kind as the main
    // one (whose title bar shows the page's title), opened on the same page in its
    // floating mode. We then do what the browser's version did: pin it on top, make it
    // small in the corner, and keep it out of Alt+Tab and off the taskbar. Its title bar
    // reads "Markdown Studio".

    static int ServePort = 0;
    static IntPtr FloatMain = IntPtr.Zero, FloatWin = IntPtr.Zero;
    static readonly object FloatLock = new object();

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr h, StringBuilder s, int max);

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint WM_CLOSE = 0x0010;

    // Our browser's visible app windows, with their titles.
    static Dictionary<IntPtr, string> OurWindows(HashSet<uint> pids)
    {
        Dictionary<IntPtr, string> all = new Dictionary<IntPtr, string>();
        StringBuilder cls = new StringBuilder(64), title = new StringBuilder(256);
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (!pids.Contains(pid)) return true;
            cls.Length = 0;
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1") return true;
            title.Length = 0;
            GetWindowText(h, title, title.Capacity);
            all[h] = title.ToString();
            return true;
        }, IntPtr.Zero);
        return all;
    }

    // Opens the floating window for the page whose channel is `id`, then finds it - the
    // new window of ours that titles itself "Markdown Studio" - and pins it in the
    // bottom-right corner of the screen the app is on.
    static void FloatOpen(string id)
    {
        string browser = FindBrowser();
        if (browser == null || ServePort == 0) return;
        try { SetProcessDPIAware(); } catch { }
        HashSet<uint> pids = OurBrowserPids();
        IntPtr fg = GetForegroundWindow();
        Dictionary<IntPtr, string> before = OurWindows(pids);
        lock (FloatLock)
        {
            if (before.ContainsKey(fg)) FloatMain = fg;
            if (FloatWin != IntPtr.Zero && IsWindow(FloatWin)) PostMessage(FloatWin, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            FloatWin = IntPtr.Zero;
        }

        string url = "http://127.0.0.1:" + ServePort + "/?k=" + key + "&pip=" + Uri.EscapeDataString(id);
        ProcessStartInfo psi = new ProcessStartInfo(browser);
        psi.Arguments = "--app=" + url + " --user-data-dir=\"" + ProfileDir + "\"" +
                        " --window-size=420,560 --no-first-run --no-default-browser-check --disable-background-mode";
        psi.UseShellExecute = false;
        try { Process.Start(psi); } catch { return; }

        Thread t = new Thread(delegate()
        {
            for (int i = 0; i < 60; i++)
            {
                Thread.Sleep(150);
                foreach (KeyValuePair<IntPtr, string> w in OurWindows(OurBrowserPidsCached(pids)))
                {
                    if (before.ContainsKey(w.Key) || w.Value != "Markdown Studio") continue;
                    PinFloat(w.Key);
                    lock (FloatLock) FloatWin = w.Key;
                    WatchFloat(w.Key);
                    return;
                }
            }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // The browser may have started a helper process for the new window; look again then.
    static HashSet<uint> OurBrowserPidsCached(HashSet<uint> known)
    {
        HashSet<uint> now = OurBrowserPids();
        now.UnionWith(known);
        return now;
    }

    static void PinFloat(IntPtr h)
    {
        double scale = 1.0;
        try { uint dpi = GetDpiForWindow(h); if (dpi > 0) scale = dpi / 96.0; } catch { }
        int w = (int)(420 * scale), hh = (int)(560 * scale), gap = (int)(16 * scale);
        System.Drawing.Rectangle wa;
        try { wa = Screen.FromHandle(FloatMain != IntPtr.Zero ? FloatMain : h).WorkingArea; }
        catch { wa = Screen.PrimaryScreen.WorkingArea; }
        w = Math.Min(w, wa.Width - 2 * gap);
        hh = Math.Min(hh, wa.Height - 2 * gap);
        SetExStyle(h, (ExStyle(h) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        SetWindowPos(h, HWND_TOPMOST, wa.Right - w - gap, wa.Bottom - hh - gap, w, hh, SWP_NOACTIVATE | SWP_FRAMECHANGED);
        try
        {
            ITaskbarList bar = (ITaskbarList)new TaskbarList();
            bar.HrInit();
            bar.DeleteTab(h);
            Marshal.ReleaseComObject(bar);
        }
        catch { }
    }

    // With no Alt+Tab entry and no taskbar button, a minimized floating window would be
    // out of reach. So minimizing it closes it; the picture-in-picture button opens it
    // again. Watched from here, because the page inside cannot tell: the browser reports
    // a tool window as hidden even while it is on screen.
    //
    // The browser also sets a window's z-order itself now and then - on focus changes,
    // for one - which can take "always on top" away again some time after it was given.
    // So the same watch puts it back whenever it has gone. Only the z-order: writing
    // the window's style again while it is up made the browser activate it and then
    // minimize it along with the main window, which closed it the moment the main
    // window was minimized - the very time it is meant to stay.
    static void WatchFloat(IntPtr h)
    {
        while (IsWindow(h))
        {
            lock (FloatLock) { if (FloatWin != h) return; }
            if (IsIconic(h)) { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return; }
            if ((ExStyle(h) & WS_EX_TOPMOST) == 0)
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Thread.Sleep(300);
        }
    }

    static void FloatClose()
    {
        IntPtr h;
        lock (FloatLock) { h = FloatWin; FloatWin = IntPtr.Zero; }
        if (h != IntPtr.Zero && IsWindow(h)) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    // "Back to Markdown Studio": the main window to the front. Windows only lets the
    // foreground process hand that over, and a key press counts as taking part - the
    // usual way round it for a helper like this one.
    static void FloatFront()
    {
        IntPtr h;
        lock (FloatLock) h = FloatMain;
        if (h == IntPtr.Zero || !IsWindow(h)) return;
        if (IsIconic(h)) ShowWindow(h, 9);              // SW_RESTORE
        keybd_event(0x12, 0, 0, UIntPtr.Zero);          // Alt down
        keybd_event(0x12, 0, 2, UIntPtr.Zero);          // Alt up
        SetForegroundWindow(h);
    }

    static int CountOurs(string profile)
    {
        int n = 0;
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
            using (ManagementObjectCollection res = s.Get())
            {
                foreach (ManagementObject mo in res)
                {
                    object cl = mo["CommandLine"];
                    if (cl != null && cl.ToString().IndexOf(profile, StringComparison.OrdinalIgnoreCase) >= 0)
                        n++;
                }
            }
        }
        catch { return 0; }
        return n;
    }

    static byte[] LoadPage()
    {
        // an index.html sitting next to the exe wins, so you can update the app
        // without rebuilding; otherwise use the copy baked into this exe
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string local = Path.Combine(dir, "index.html");
        if (File.Exists(local)) return File.ReadAllBytes(local);

        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.html"))
        {
            if (s == null)
                throw new Exception("index.html was not found next to the program, and " +
                                    "no copy is embedded in it.");
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[16384];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
            return ms.ToArray();
        }
    }

    static string FindBrowser()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string px = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] candidates = {
            Path.Combine(px, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(px, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe")
        };
        foreach (string c in candidates) if (File.Exists(c)) return c;
        return null;
    }

    // ---------------------------------------------------------------- http

    static void Serve(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch { return; }
            ThreadPool.QueueUserWorkItem(delegate(object o) { Handle((TcpClient)o); }, client);
        }
    }

    // Reads until the blank line that ends the headers, then exactly as many body
    // bytes as Content-Length promises. The old single-read version was fine for
    // GET but would truncate a document on the way back to disk.
    static bool ReadRequest(NetworkStream ns, out string head, out byte[] body)
    {
        head = null; body = new byte[0];
        MemoryStream buf = new MemoryStream();
        byte[] chunk = new byte[8192];
        int split = -1;

        while (split < 0)
        {
            int n = ns.Read(chunk, 0, chunk.Length);
            if (n <= 0) return false;
            buf.Write(chunk, 0, n);
            byte[] all = buf.ToArray();
            for (int i = 3; i < all.Length; i++)
                if (all[i] == 10 && all[i - 1] == 13 && all[i - 2] == 10 && all[i - 3] == 13)
                { split = i + 1; break; }
            if (buf.Length > 65536) return false;
        }

        byte[] raw = buf.ToArray();
        head = Encoding.UTF8.GetString(raw, 0, split);

        int len = 0;
        foreach (string line in head.Split('\n'))
        {
            string l = line.Trim();
            if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(l.Substring(15).Trim(), out len);
        }
        if (len <= 0) return true;
        if (len > 64 * 1024 * 1024) return false;

        MemoryStream bs = new MemoryStream();
        bs.Write(raw, split, raw.Length - split);
        while (bs.Length < len)
        {
            int n = ns.Read(chunk, 0, (int)Math.Min(chunk.Length, len - bs.Length));
            if (n <= 0) break;
            bs.Write(chunk, 0, n);
        }
        body = bs.ToArray();
        return true;
    }

    static string QueryValue(string query, string name)
    {
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (pair.Substring(0, eq) != name) continue;
            try { return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace("+", "%20")); }
            catch { return null; }
        }
        return null;
    }

    static void Handle(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
                string head;
                byte[] body;
                if (!ReadRequest(ns, out head, out body)) return;

                string line = head.Split('\n')[0].Trim();
                string[] parts = line.Split(' ');
                if (parts.Length < 2) return;
                string method = parts[0];
                string target = parts[1];

                string path = target, query = "";
                int q = target.IndexOf('?');
                if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }

                if (path == "/" || path == "/index.html")
                { Send(ns, "200 OK", "text/html; charset=utf-8", page); return; }

                if (path == "/__ws" || path == "/__file" || path == "/__all" ||
                    path == "/__launch" || path == "/__libdir" || path == "/__reveal" ||
                    path == "/__asset" || path == "/__entry" || path == "/__float")
                {
                    if (QueryValue(query, "k") != key)
                    { SendText(ns, "403 Forbidden", "forbidden"); return; }

                    if (path == "/__launch")
                    {
                        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
                        SendText(ns, "200 OK", Launch(body));
                        return;
                    }

                    if (path == "/__float")
                    {
                        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
                        string op = QueryValue(query, "op") ?? "";
                        if (op == "open")
                        {
                            string id = QueryValue(query, "id") ?? "";
                            bool okId = id.Length > 0 && id.Length <= 40;
                            foreach (char c in id) if (!char.IsLetterOrDigit(c)) okId = false;
                            if (!okId) { SendText(ns, "400 Bad Request", "bad id"); return; }
                            FloatOpen(id);
                        }
                        else if (op == "close") FloatClose();
                        else if (op == "front") FloatFront();
                        else { SendText(ns, "400 Bad Request", "unknown operation"); return; }
                        SendText(ns, "200 OK", "ok");
                        return;
                    }

                    if (path == "/__libdir")
                    {
                        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
                        Send(ns, "200 OK", "application/json; charset=utf-8",
                             Encoding.UTF8.GetBytes(ChooseLibrary()));
                        return;
                    }

                    // Showing the library in Explorer is the launcher's to do: a page
                    // cannot open a folder, and the fixed library is the only thing this
                    // opens, so it cannot be talked into showing anything else.
                    if (path == "/__reveal")
                    {
                        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
                        try
                        {
                            string dir;
                            lock (workspaces) dir = workspaces[LibId].Root;
                            Directory.CreateDirectory(dir);
                            ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "\"" + dir + "\"");
                            psi.UseShellExecute = true;
                            Process.Start(psi);
                            SendText(ns, "200 OK", "ok");
                        }
                        catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
                        return;
                    }

                    Workspace ws = GetWorkspace(QueryValue(query, "w"));

                    if (path == "/__ws")
                    { Send(ns, "200 OK", "application/json; charset=utf-8",
                           Encoding.UTF8.GetBytes(Manifest(ws))); return; }

                    if (path == "/__all")
                    { Send(ns, "200 OK", "application/json; charset=utf-8",
                           Encoding.UTF8.GetBytes(AllFiles(ws))); return; }

                    if (path == "/__asset") { Asset(ns, method, ws, QueryValue(query, "p"), body); return; }
                    if (path == "/__entry")
                    {
                        Entry(ns, method, ws, QueryValue(query, "p"), QueryValue(query, "op"), QueryValue(query, "to"));
                        return;
                    }

                    string full = Resolve(ws, QueryValue(query, "p"));
                    if (full == null) { SendText(ns, "400 Bad Request", "bad path"); return; }

                    if (method == "GET")
                    {
                        if (!File.Exists(full)) { SendText(ns, "404 Not Found", "no such file"); return; }
                        try
                        {
                            Send(ns, "200 OK", "text/plain; charset=utf-8", File.ReadAllBytes(full));
                        }
                        catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
                        return;
                    }
                    if (method == "POST" || method == "PUT")
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(full);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllBytes(full, body);
                            SendText(ns, "200 OK", "ok");
                        }
                        catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
                        return;
                    }
                    // renaming a document is a write under the new name and this under
                    // the old one, so the folder does not fill up with stale copies
                    if (method == "DELETE")
                    {
                        try
                        {
                            if (!File.Exists(full)) { SendText(ns, "404 Not Found", "no such file"); return; }
                            Recycle(full);
                            SendText(ns, "200 OK", "ok");
                        }
                        catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
                        return;
                    }
                    SendText(ns, "405 Method Not Allowed", "no");
                    return;
                }

                SendText(ns, "404 Not Found", "Not found");
            }
        }
        catch { }
    }

    // What the Explorer tree shows, and so all that its right-click menu may touch:
    // documents, and anything inside an assets folder.
    static bool InTree(string rel) { return IsMd(rel) || InAssets(rel); }

    static bool BadName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return true;
        if (name == "." || name == ".." || name.EndsWith(".") || name.EndsWith(" ")) return true;
        return name.IndexOfAny(new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 ||
               name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    // The right-click menu's file operations. A rename stays in the same folder and
    // must leave the file something the tree still shows - a document stays a
    // document, an asset stays in its assets folder - and never lands on another
    // file. A delete goes to the Recycle Bin. Reveal opens Explorer on the file.
    static void Entry(NetworkStream ns, string method, Workspace ws, string rel, string op, string to)
    {
        // New Folder: p is the folder it goes in ("" for the top), to is its name
        if (op == "mkdir")
        {
            if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
            string parent = ResolveDir(ws, rel);
            if (parent == null) { SendText(ns, "400 Bad Request", "bad path"); return; }
            if (!Directory.Exists(parent)) { SendText(ns, "404 Not Found", "That folder is not there any more."); return; }
            if (BadName(to) || to.StartsWith(".") || SkipDir(to))
            { SendText(ns, "400 Bad Request", "That is not a valid folder name."); return; }
            string made = Path.Combine(parent, to);
            if (Directory.Exists(made) || File.Exists(made))
            { SendText(ns, "409 Conflict", "Something named " + to + " is already there."); return; }
            try
            {
                Directory.CreateDirectory(made);
                string parentRel = (rel ?? "").Replace('\\', '/').Trim('/');
                string madeRel = (parentRel.Length > 0 ? parentRel + "/" : "") + to;
                Send(ns, "200 OK", "application/json; charset=utf-8",
                     Encoding.UTF8.GetBytes("{\"path\":" + JsonStr(madeRel) + "}"));
            }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }

        string full = Resolve(ws, rel, InTree);
        if (full == null) { SendText(ns, "400 Bad Request", "bad path"); return; }
        if (!File.Exists(full)) { SendText(ns, "404 Not Found", "no such file"); return; }

        if (method == "DELETE")
        {
            try { Recycle(full); SendText(ns, "200 OK", "ok"); }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }

        if (op == "reveal")
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "/select,\"" + full + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
                SendText(ns, "200 OK", "ok");
            }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        if (op == "rename")
        {
            if (BadName(to)) { SendText(ns, "400 Bad Request", "That is not a valid file name."); return; }
            string dirRel = rel.Contains("/") ? rel.Substring(0, rel.LastIndexOf('/') + 1) : "";
            string newRel = dirRel + to;
            string dest = Resolve(ws, newRel, InTree);
            if (dest == null)
            {
                SendText(ns, "400 Bad Request", IsMd(rel)
                    ? "A document has to keep a Markdown extension such as .md."
                    : "That name cannot be used here.");
                return;
            }
            bool sameFile = string.Equals(dest, full, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(dest) && !sameFile)
            { SendText(ns, "409 Conflict", "A file named " + to + " is already there."); return; }
            try
            {
                if (sameFile)
                {
                    // only the letter case changes, which Windows will not do in one move
                    string tmp = full + ".rename-" + NewKey().Substring(0, 6);
                    File.Move(full, tmp);
                    File.Move(tmp, dest);
                }
                else File.Move(full, dest);
                Send(ns, "200 OK", "application/json; charset=utf-8",
                     Encoding.UTF8.GetBytes("{\"path\":" + JsonStr(newRel) + "}"));
            }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        // Dragged into another folder in the Explorer: to is that folder ("" for the
        // top). The file keeps its name, never lands on another file, and stays
        // something the tree shows - a picture from an assets folder only goes into an
        // assets folder.
        if (op == "move")
        {
            string destDir = ResolveDir(ws, to);
            if (destDir == null) { SendText(ns, "400 Bad Request", "bad folder"); return; }
            if (!Directory.Exists(destDir)) { SendText(ns, "404 Not Found", "That folder is not there any more."); return; }
            string toRel = (to ?? "").Replace('\\', '/').Trim('/');
            string name = Path.GetFileName(full);
            string newRel = (toRel.Length > 0 ? toRel + "/" : "") + name;
            string dest = Resolve(ws, newRel, InTree);
            if (dest == null)
            { SendText(ns, "400 Bad Request", "Pictures and other files in an assets folder can only go into an assets folder."); return; }
            if (string.Equals(dest, full, StringComparison.OrdinalIgnoreCase))
            {
                Send(ns, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"path\":" + JsonStr(newRel) + "}"));
                return;
            }
            if (File.Exists(dest)) { SendText(ns, "409 Conflict", "A file named " + name + " is already there."); return; }
            try
            {
                File.Move(full, dest);
                Send(ns, "200 OK", "application/json; charset=utf-8",
                     Encoding.UTF8.GetBytes("{\"path\":" + JsonStr(newRel) + "}"));
            }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        SendText(ns, "400 Bad Request", "unknown operation");
    }

    // A picture beside a document. GET shows it in the preview; POST is a pasted or
    // dropped one being saved, and answers where it actually went - a name that is
    // already taken is never overwritten, the new picture gets a free one instead.
    static void Asset(NetworkStream ns, string method, Workspace ws, string rel, byte[] body)
    {
        string full = Resolve(ws, rel, IsImage);
        if (full == null) { SendText(ns, "400 Bad Request", "bad path"); return; }

        if (method == "GET")
        {
            if (!File.Exists(full)) { SendText(ns, "404 Not Found", "no such file"); return; }
            // An SVG opened on its own would run any script inside it with this
            // page's origin; as a picture in the preview it never does. So it is
            // only ever let out as a picture.
            string extra = "X-Content-Type-Options: nosniff\r\n";
            if (full.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                extra += "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox\r\n";
            try { Send(ns, "200 OK", ImageType(full), File.ReadAllBytes(full), extra); }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        if (method == "POST" || method == "PUT")
        {
            if (body.Length == 0) { SendText(ns, "400 Bad Request", "empty"); return; }
            try
            {
                string relOut = RelOf(ws, full);
                if (File.Exists(full))
                {
                    relOut = Free(ws.Root, relOut);
                    if (relOut == null) { SendText(ns, "409 Conflict", "no free name"); return; }
                    full = Path.Combine(ws.Root, relOut.Replace('/', '\\'));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, body);
                Send(ns, "200 OK", "application/json; charset=utf-8",
                     Encoding.UTF8.GetBytes("{\"path\":" + JsonStr(relOut) + "}"));
            }
            catch (Exception ex) { SendText(ns, "500 Internal Server Error", ex.Message); }
            return;
        }
        SendText(ns, "405 Method Not Allowed", "no");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    // A deleted document goes to the Recycle Bin rather than being wiped. These are
    // the user's own notes in their own Documents folder, so a delete clicked by
    // mistake should be something Windows can undo.
    static void Recycle(string path)
    {
        SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
        op.wFunc = 3;                          // FO_DELETE
        op.pFrom = path + "\0\0";
        op.fFlags = 0x0040 | 0x0010 | 0x0004;  // ALLOWUNDO | NOCONFIRMATION | SILENT
        if (SHFileOperation(ref op) != 0) File.Delete(path);
    }

    static void SendText(NetworkStream ns, string status, string text)
    {
        Send(ns, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
    }

    static void Send(NetworkStream ns, string status, string type, byte[] body)
    {
        Send(ns, status, type, body, "");
    }

    static void Send(NetworkStream ns, string status, string type, byte[] body, string extra)
    {
        string head =
            "HTTP/1.1 " + status + "\r\n" +
            "Content-Type: " + type + "\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Cache-Control: no-store\r\n" + extra +
            "Connection: close\r\n\r\n";
        byte[] hb = Encoding.ASCII.GetBytes(head);
        ns.Write(hb, 0, hb.Length);
        ns.Write(body, 0, body.Length);
        ns.Flush();
    }
}
