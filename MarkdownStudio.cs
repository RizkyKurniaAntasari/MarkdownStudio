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
// file at once.
//
// The port is fixed, because the browser keys everything it stores (the Docs,
// settings, the remembered folder) to the origin, and the origin includes the
// port - a random port per launch quietly started every session empty. Only one
// copy can own that port, so a second launch hands its files to the running copy
// and just opens another window on it.
//
// Every launch also registers the app for "Open with" on Markdown files, in the
// current user's registry only. --register does just that, --unregister undoes it.
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
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

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

    const int MaxDepth = 6;
    const int MaxFiles = 3000;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            foreach (string a in args)
            {
                if (a == "--register") { Register(); return 0; }
                if (a == "--unregister") { Unregister(); return 0; }
            }
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

    static void Run(string[] args)
    {
        page = LoadPage();
        try { Register(); } catch { }      // never worth failing a launch over
        Workspace ws = TakeWorkspace(args);

        string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownStudio", "profile");

        // already running: that copy serves this window too, so its origin (and
        // everything stored under it) is the same one you had open before
        string handed = HandOff(ws);
        if (handed != null)
        {
            OpenWindow(handed, profile, false);
            return;
        }

        key = NewKey();
        string id = AddWorkspace(ws);
        TcpListener listener = Listen();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

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
    static void Register()
    {
        string exe = Assembly.GetExecutingAssembly().Location;
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
            }
        }
        // tell Explorer, or the menu keeps showing the old state until a restart
        if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    static void Unregister()
    {
        string file = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        using (RegistryKey c = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
        {
            c.DeleteSubKeyTree(ProgId, false);
            c.DeleteSubKeyTree(@"Applications\" + file, false);
            foreach (string e in MdExts)
            {
                using (RegistryKey k = c.OpenSubKey(e + @"\OpenWithProgids", true))
                    if (k != null) k.DeleteValue(ProgId, false);
            }
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    // ---------------------------------------------------------------- workspace

    static bool IsMd(string path)
    {
        string e = Path.GetExtension(path).ToLowerInvariant();
        return e == ".md" || e == ".markdown" || e == ".mdown" || e == ".mkd" ||
               e == ".mdwn" || e == ".mdx" || e == ".txt";
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
        if (depth > MaxDepth || into.Count >= MaxFiles) return;
        string[] entries;
        try { entries = Directory.GetFiles(dir); } catch { return; }
        foreach (string f in entries)
        {
            if (into.Count >= MaxFiles) return;
            if (IsMd(f)) into.Add(prefix + Path.GetFileName(f));
        }
        string[] subs;
        try { subs = Directory.GetDirectories(dir); } catch { return; }
        foreach (string s in subs)
        {
            string name = Path.GetFileName(s);
            if (name.StartsWith(".") || SkipDir(name)) continue;
            Walk(s, prefix + name + "/", depth + 1, into);
        }
    }

    // Resolves a relative path from the page against the workspace, refusing
    // anything that escapes it or is not a text file we handle.
    static string Resolve(Workspace ws, string rel)
    {
        if (ws == null || string.IsNullOrEmpty(rel)) return null;
        rel = rel.Replace('\\', '/');
        if (rel.StartsWith("/") || rel.Contains(":")) return null;
        // a ".." segment climbs out; "notes..md" is just a name and is fine
        foreach (string seg in rel.Split('/')) if (seg == "..") return null;
        if (!IsMd(rel)) return null;

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

                if (path == "/__ws" || path == "/__file" || path == "/__all" || path == "/__launch")
                {
                    if (QueryValue(query, "k") != key)
                    { SendText(ns, "403 Forbidden", "forbidden"); return; }

                    if (path == "/__launch")
                    {
                        if (method != "POST") { SendText(ns, "405 Method Not Allowed", "no"); return; }
                        SendText(ns, "200 OK", Launch(body));
                        return;
                    }

                    Workspace ws = GetWorkspace(QueryValue(query, "w"));

                    if (path == "/__ws")
                    { Send(ns, "200 OK", "application/json; charset=utf-8",
                           Encoding.UTF8.GetBytes(Manifest(ws))); return; }

                    if (path == "/__all")
                    { Send(ns, "200 OK", "application/json; charset=utf-8",
                           Encoding.UTF8.GetBytes(AllFiles(ws))); return; }

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
                    SendText(ns, "405 Method Not Allowed", "no");
                    return;
                }

                SendText(ns, "404 Not Found", "Not found");
            }
        }
        catch { }
    }

    static void SendText(NetworkStream ns, string status, string text)
    {
        Send(ns, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
    }

    static void Send(NetworkStream ns, string status, string type, byte[] body)
    {
        string head =
            "HTTP/1.1 " + status + "\r\n" +
            "Content-Type: " + type + "\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";
        byte[] hb = Encoding.ASCII.GetBytes(head);
        ns.Write(hb, 0, hb.Length);
        ns.Write(body, 0, body.Length);
        ns.Flush();
    }
}
