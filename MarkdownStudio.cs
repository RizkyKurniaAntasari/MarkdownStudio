// Markdown Studio - desktop launcher
//
// Serves the bundled index.html from a loopback socket and opens it in Edge (or
// Chrome) as an app window. Going through http:// rather than file:// is the whole
// point: browsers only grant the File System Access API on an http(s) origin, so
// this is what makes "Open Folder" and "save straight back to disk" work without
// needing laragon running.
//
// Build:  build-exe.cmd

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Management;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class MarkdownStudio
{
    static byte[] page;

    [STAThread]
    static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Markdown Studio could not start.\n\n" + ex.Message,
                "Markdown Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static void Run()
    {
        page = LoadPage();

        // port 0 = let Windows pick a free one, so two copies never collide
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Thread server = new Thread(delegate() { Serve(listener); });
        server.IsBackground = true;
        server.Start();

        string url = "http://127.0.0.1:" + port + "/";
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
        Directory.CreateDirectory(profile);

        ProcessStartInfo psi = new ProcessStartInfo(browser);
        psi.Arguments =
            "--app=" + url +
            " --user-data-dir=\"" + profile + "\"" +
            " --window-size=1360,900" +
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

    static void Handle(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
                byte[] buf = new byte[8192];
                int n = ns.Read(buf, 0, buf.Length);
                if (n <= 0) return;

                string req = Encoding.ASCII.GetString(buf, 0, n);
                string path = "/";
                int a = req.IndexOf(' ');
                if (a > 0)
                {
                    int b = req.IndexOf(' ', a + 1);
                    if (b > a) path = req.Substring(a + 1, b - a - 1);
                }
                int q = path.IndexOf('?');
                if (q >= 0) path = path.Substring(0, q);

                byte[] body;
                string type;
                string status;
                if (path == "/" || path == "/index.html")
                {
                    body = page;
                    type = "text/html; charset=utf-8";
                    status = "200 OK";
                }
                else
                {
                    body = Encoding.UTF8.GetBytes("Not found");
                    type = "text/plain; charset=utf-8";
                    status = "404 Not Found";
                }

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
        catch { }
    }
}
