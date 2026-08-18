using Knapcode.TorSharp;
using Manta.Remote.Models;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Manta.Remote.Services;

public class TorService
{
    private readonly TorSharpSettings _settings;
    private string _torVersion = string.Empty;
    private string _torExecutablePath = string.Empty;

    public TorService()
    {
        _settings = new TorSharpSettings
        {
            ZippedToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor"),
            ExtractedToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor"),
            PrivoxySettings = 
            { 
                Disable = true 
            },
            TorSettings = new TorSharpTorSettings
            {
                
            },
        };
    }

    public async Task EnsureTorInstalledAsync()
    {
        Console.WriteLine("Checking Tor installation...");

        // TorSharp does not support macOS, use the official tor expert bundle or a system-installed tor
        if (OperatingSystem.IsMacOS())
        {
            _torExecutablePath = await EnsureMacTorInstalledAsync()
                ?? FindSystemTor()
                ?? throw new Exception("Could not install tor. Install it manually with: brew install tor");
            Console.WriteLine("Using tor at " + _torExecutablePath);
            return;
        }

        using var httpClient = new HttpClient();
        var fetcher = new TorSharpToolFetcher(_settings, httpClient);
        
        try
        {
            var update = await fetcher.CheckForUpdatesAsync();

            if (update.Tor.Status == ToolUpdateStatus.NoUpdateAvailable)
            {
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor installed and up to date");
                return;
            }

            if (update.Tor.Status == ToolUpdateStatus.NoLocalVersion)
            {
                Console.WriteLine("Tor not installed, will install now");

                await fetcher.FetchAsync();

                using var proxy = new TorSharpProxy(_settings);
                await proxy.ConfigureAndStartAsync();
                proxy.Stop();

                update = await fetcher.CheckForUpdatesAsync();
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor installed successfully");
            }
            else if (update.Tor.Status == ToolUpdateStatus.NewerVersionAvailable)
            {
                Console.WriteLine("Tor has available update, will update now");

                await fetcher.FetchAsync();

                using var proxy = new TorSharpProxy(_settings);
                await proxy.ConfigureAndStartAsync();
                proxy.Stop();

                update = await fetcher.CheckForUpdatesAsync();
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor updated successfully");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    [SupportedOSPlatform("macos")]
    private static async Task<string?> EnsureMacTorInstalledAsync()
    {
        var bundleDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor", "bundle");
        var torBinaryPath = Path.Combine(bundleDirectory, "tor", "tor");
        var versionFilePath = Path.Combine(bundleDirectory, "version");

        using var httpClient = new HttpClient();

        string? latestVersion = null;
        try
        {
            using var downloads = JsonDocument.Parse(await httpClient.GetStringAsync("https://aus1.torproject.org/torbrowser/update_3/release/download-macos.json"));
            latestVersion = downloads.RootElement.GetProperty("version").GetString();
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not check latest tor version: " + e.Message);
        }

        var installedVersion = File.Exists(versionFilePath) ? File.ReadAllText(versionFilePath) : null;

        if (File.Exists(torBinaryPath) && (latestVersion is null || latestVersion == installedVersion))
        {
            Console.WriteLine("Tor installed" + (latestVersion is null ? "" : " and up to date"));
            return torBinaryPath;
        }

        if (latestVersion is null)
            return null;

        Console.WriteLine(installedVersion is null ? "Tor not installed, will install now" : "Tor has available update, will update now");

        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
        var url = $"https://dist.torproject.org/torbrowser/{latestVersion}/tor-expert-bundle-macos-{arch}-{latestVersion}.tar.gz";

        try
        {
            Directory.CreateDirectory(bundleDirectory);

            using var stream = await httpClient.GetStreamAsync(url);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, bundleDirectory, overwriteFiles: true);

            // The bundle ships unsigned binaries; ad-hoc sign them or macOS kills them on launch
            foreach (var file in Directory.GetFiles(Path.Combine(bundleDirectory, "tor")))
            {
                using var codesign = Process.Start(new ProcessStartInfo { FileName = "/usr/bin/codesign", Arguments = $"-f -s - \"{file}\"", UseShellExecute = false });
                await codesign!.WaitForExitAsync();

                if (codesign.ExitCode != 0)
                    throw new Exception("Could not sign " + file);
            }

            File.WriteAllText(versionFilePath, latestVersion);

            Console.WriteLine("Tor installed successfully");
            return torBinaryPath;
        }
        catch (Exception e)
        {
            Console.WriteLine("Tor download failed: " + e.Message);
            return File.Exists(torBinaryPath) ? torBinaryPath : null;
        }
    }

    private static string? FindSystemTor()
    {
        string[] candidates = ["/opt/homebrew/bin/tor", "/usr/local/bin/tor", "/opt/local/bin/tor"];
        var pathDirectories = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? [];
        return candidates.Concat(pathDirectories.Select(p => Path.Combine(p, "tor"))).FirstOrDefault(File.Exists);
    }

    public void SetupHiddenService()
    {
        string hiddenServiceParameter = "HiddenServicePort 2134 127.0.0.1:2134";
        string hiddenServiceDir = $"HiddenServiceDir {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HiddenService")}";
        //string hiddenServiceParameter = "HiddenServicePort 9998 unix:/var/run/tor/my-website.sock;

        if (OperatingSystem.IsMacOS())
        {
            // Tor creates the data and hidden service directories itself with the required 700 permissions
            var torDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor");
            Directory.CreateDirectory(torDirectory);

            File.WriteAllText(Path.Combine(torDirectory, "torrc"),
                "SocksPort 0\n" +
                $"Log notice file {Path.Combine(torDirectory, "tor.log")}\n" +
                $"DataDirectory {Path.Combine(torDirectory, "data")}\n" +
                hiddenServiceDir + "\n" +
                hiddenServiceParameter + "\n");

            return;
        }

        string torFolderName;

        switch (_settings.OSPlatform)
        {
            case TorSharpOSPlatform.Linux:
                torFolderName = _settings.Architecture == TorSharpArchitecture.X64 ? "tor-linux64-" : "tor-linux32-";
                break;
            case TorSharpOSPlatform.Windows:
                torFolderName = _settings.Architecture == TorSharpArchitecture.X64 ? "tor-win64-" : "tor-win32-";
                break;
            default: throw new NotSupportedException("Platform not supported");
        }

        var torrcPath = Path.Combine(_settings.ExtractedToolsDirectory, $"{torFolderName}{_torVersion}", "data", "tor", "torrc");
        using var fileStream = File.Open(torrcPath, FileMode.Open, FileAccess.ReadWrite);

        using StreamReader reader = new(fileStream);
        string input = reader.ReadToEnd();

        if (input.Contains(hiddenServiceParameter))
            return;

        using StreamWriter writer = new(fileStream);
        {
            writer.Write("\n" + hiddenServiceDir + "\n");
            writer.Write(hiddenServiceParameter + "\n");
        }

        writer.Close();
    }

    public async Task StartHiddenServiceAsync()
    {
        if (OperatingSystem.IsMacOS())
        {
            await StartMacTorAsync();
            return;
        }

        var proxy = new TorSharpProxy(_settings);
        await proxy.ConfigureAndStartAsync();
    }

    private async Task StartMacTorAsync()
    {
        var torrcPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor", "torrc");

        var torProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _torExecutablePath,
            Arguments = $"-f \"{torrcPath}\" --hush",
            UseShellExecute = false
        }) ?? throw new Exception("Could not start tor");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { torProcess.Kill(); } catch { } };

        // Wait for tor to write the hidden service hostname
        var hostnameFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HiddenService", "hostname");

        for (int i = 0; i < 120; i++)
        {
            if (File.Exists(hostnameFile))
                return;

            if (torProcess.HasExited)
                throw new Exception($"Tor exited unexpectedly with code {torProcess.ExitCode}, see Tor/tor.log");

            await Task.Delay(500);
        }

        throw new Exception("Timed out waiting for tor to create the hidden service");
    }

    public string GetOnionAddress()
    {
        string hostnameFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HiddenService", "hostname");
        using var fileStream = File.Open(hostnameFile, FileMode.Open, FileAccess.Read);
        using StreamReader reader = new(fileStream);
        return reader.ReadToEnd();
    }

    public async Task StartOutgoingTorProxy()
    {
        Console.WriteLine("Starting Tor");

        using var proxy = new TorSharpProxy(_settings);
        await proxy.ConfigureAndStartAsync();

        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(new Uri("socks5://localhost:" + _settings.TorSettings.SocksPort))
        };

        using (handler)
        using (var httpClient = new HttpClient(handler))
        {
            var result = await httpClient.GetFromJsonAsync<TorResponse>("https://check.torproject.org/api/ip");

            Console.WriteLine("IP address: " + result?.IP);
            Console.WriteLine("Is Tor: " + result?.IsTor);
        }

        proxy.Stop();
    }
}
