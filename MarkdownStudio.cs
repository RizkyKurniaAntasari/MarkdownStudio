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
// go through /__ws and /__file here.
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class MarkdownStudio
{
    static byte[] page;

    // workspace handed over from the command line - null when launched with no file
    static string wsRoot;                 // full path of the folder we serve
    static string wsName = "";            // its display name
    static List<string> wsOpen = new List<string>();   // files to open on start
    static string key = "";               // guards the workspace endpoints

    const int MaxDepth = 6;
    const int MaxFiles = 3000;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
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
        TakeWorkspace(args);
        key = NewKey();

        // port 0 = let Windows pick a free one, so two copies never collide
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Thread server = new Thread(delegate() { Serve(listener); });
        server.IsBackground = true;
        server.Start();

        string url = "http://127.0.0.1:" + port + "/?k=" + key;
        string browser = FindBrowser();

        if (browser == null)
        {
            // no Chromium browser found - fall back to whatever handles http
            Process.Start(url);
            MessageBox.Show("Markdown Studio is running at\n" + url +
                "\n\nClose this dialog when you are done.",
                "Markdown Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // its own profile folder, so the app window is independent of your normal
        // browsing session and keeps its own documents and settings
        string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownStudio", "profile");
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
        if (proc == null) return;

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
    // still resolve.
    static void TakeWorkspace(string[] args)
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
        if (dir == null) return;

        wsRoot = dir.TrimEnd('\\', '/');
        wsName = Path.GetFileName(wsRoot);
        if (wsName.Length == 0) wsName = wsRoot;          // a drive root has no name

        foreach (string f in files)
        {
            string rel = RelOf(f);
            // a file dropped on us from elsewhere still opens, just not as part of
            // the tree - but since we took its own folder as the root, it always is
            if (rel != null) wsOpen.Add(rel);
        }
    }

    static string RelOf(string full)
    {
        string r = wsRoot + Path.DirectorySeparatorChar;
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
    static string Resolve(string rel)
    {
        if (wsRoot == null || string.IsNullOrEmpty(rel)) return null;
        rel = rel.Replace('\\', '/');
        if (rel.StartsWith("/") || rel.Contains("..") || rel.Contains(":")) return null;
        if (!IsMd(rel)) return null;

        string full;
        try { full = Path.GetFullPath(Path.Combine(wsRoot, rel.Replace('/', '\\'))); }
        catch { return null; }

        string r = wsRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
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

    static string Manifest()
    {
        if (wsRoot == null) return "{\"root\":null}";
        List<string> files = new List<string>();
        Walk(wsRoot, "", 0, files);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        StringBuilder b = new StringBuilder();
        b.Append("{\"root\":").Append(JsonStr(wsName));
        b.Append(",\"path\":").Append(JsonStr(wsRoot));
        b.Append(",\"files\":[");
        for (int i = 0; i < files.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(files[i]));
        }
        b.Append("],\"open\":[");
        for (int i = 0; i < wsOpen.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(JsonStr(wsOpen[i]));
        }
        b.Append("]}");
        return b.ToString();
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

                if (path == "/__ws" || path == "/__file")
                {
                    if (QueryValue(query, "k") != key)
                    { SendText(ns, "403 Forbidden", "forbidden"); return; }

                    if (path == "/__ws")
                    { Send(ns, "200 OK", "application/json; charset=utf-8",
                           Encoding.UTF8.GetBytes(Manifest())); return; }

                    string full = Resolve(QueryValue(query, "p"));
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
