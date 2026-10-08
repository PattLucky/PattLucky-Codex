using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("PattLucky Codex")]
[assembly: AssemblyDescription("Local AI desktop chat workspace")]
[assembly: AssemblyCompany("PattLucky")]
[assembly: AssemblyProduct("PattLucky Codex")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class PattLuckyCodex
{
    private const string AppUrl = "http://127.0.0.1:43771/";

    [STAThread]
    private static int Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string serverScript = Path.Combine(root, "launch.ps1");
        string edgePath = FindEdge();
        if (!File.Exists(serverScript) || !File.Exists(Path.Combine(root, "app.html")))
        {
            MessageBox.Show("PattLucky Codex files are missing. Reinstall it using the setup wizard.", "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        if (String.IsNullOrEmpty(edgePath))
        {
            MessageBox.Show("Microsoft Edge is required to display PattLucky Codex. Install or update Edge, then try again.", "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Process server = null;
        Process window = null;
        bool ownsServer = false;
        try
        {
            if (!WaitForServer(500))
            {
                ProcessStartInfo serverInfo = new ProcessStartInfo("powershell.exe");
                serverInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(serverScript) + " -AppMode";
                serverInfo.WorkingDirectory = root;
                serverInfo.UseShellExecute = false;
                serverInfo.CreateNoWindow = true;
                server = Process.Start(serverInfo);
                ownsServer = true;
                if (!WaitForServer(20000))
                {
                    MessageBox.Show("The local app server did not start. Close any other PattLucky Codex window and try again.", "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }

            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PattLucky Codex", "BrowserProfile");
            Directory.CreateDirectory(profile);
            ProcessStartInfo edgeInfo = new ProcessStartInfo(edgePath);
            edgeInfo.Arguments = "--app=" + AppUrl + " --user-data-dir=" + Quote(profile);
            edgeInfo.WorkingDirectory = root;
            window = Process.Start(edgeInfo);
            if (window == null) throw new InvalidOperationException("Microsoft Edge could not be started.");
            window.WaitForExit();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("PattLucky Codex could not open. " + ex.Message, "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (ownsServer) StopLocalServer();
            try
            {
                if (ownsServer && server != null && !server.HasExited)
                {
                    if (!server.WaitForExit(5000)) server.Kill();
                }
            }
            catch { }
            if (server != null) server.Dispose();
            if (window != null) window.Dispose();
        }
    }

    private static bool WaitForServer(int timeoutMs)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(AppUrl);
                request.Timeout = 500;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return response.StatusCode == HttpStatusCode.OK;
            }
            catch { Thread.Sleep(250); }
        }
        return false;
    }

    private static void StopLocalServer()
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(AppUrl + "api/shutdown");
            request.Method = "POST";
            request.ContentLength = 0;
            request.Timeout = 2000;
            using (request.GetResponse()) { }
        }
        catch { }
    }

    private static string FindEdge()
    {
        string[] candidates = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
