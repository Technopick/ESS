using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace EssPortalSetup;

/// <summary>
/// One-file setup for the ESS portal. It unpacks the embedded win-x64 package, asks for the settings, then runs the
/// package's own PowerShell scripts (Initialize-EssDatabase.ps1 and Install-EssPortal.ps1), so the install logic lives in
/// one place. Running it again on an installed server updates the portal: the database script is idempotent and the
/// install script keeps the generated signing key.
/// Secrets are handed to PowerShell through environment variables of the child process only, never on a command line,
/// in the answers file or in the log.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private static readonly string WorkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EssPortalSetup");
    private static readonly string AnswersFile = Path.Combine(WorkRoot, "answers.json");
    private const string DefaultInstallRoot = @"C:\inetpub\ess-portal";
    private const string HostingBundleUrl = "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        Title($"ESS Portal Setup {version}");
        if (!OperatingSystem.IsWindows()) { Error("This setup runs on Windows Server only."); return 1; }

        try
        {
            var package = Unpack(version);
            var previous = Answers.Load(AnswersFile);
            var existing = Directory.Exists(Path.Combine(previous.InstallRoot ?? DefaultInstallRoot, "app"));
            Info(existing
                ? "An existing installation was found. Setup will update it and keep its data. Press Enter to keep the values in [brackets]."
                : "This installs the portal, its IIS site and its PostgreSQL database. Press Enter to accept the values in [brackets].");

            var a = Ask(previous, existing);
            Console.WriteLine();
            Title("Summary");
            Console.WriteLine($"  Environment      {a.Environment}");
            Console.WriteLine($"  Website          {a.Url}  (HTTPS port {(a.HttpsPort == 0 ? 443 : a.HttpsPort)})");
            Console.WriteLine($"  HTTPS cert       {(a.CreateSelfSigned ? "new self-signed certificate (testing only)" : a.CertificateThumbprint)}");
            Console.WriteLine($"  Database         PostgreSQL on {a.DbServer}, port {a.DbPort}, database {a.DbName}, user {a.DbUser}");
            Console.WriteLine($"  Install folder   {a.InstallRoot}   data: {a.DataRoot}");
            Console.WriteLine($"  NAV sign-in      {(string.IsNullOrEmpty(a.ServiceAccount) ? "user name and password (set later in the portal)" : "Windows account " + a.ServiceAccount)}");
            Console.WriteLine($"  E-mail           {(string.IsNullOrEmpty(a.SmtpHost) ? "not configured" : a.SmtpHost)}");
            Console.WriteLine($"  SMS              {(a.SmsProvider == "MobileSasa" ? "Mobile Sasa, sender " + a.SmsSenderId : "not configured")}");
            Console.WriteLine($"  First admin      {(string.IsNullOrEmpty(a.AdminUser) ? "no" : a.AdminUser)}");
            if (!Confirm("Install now?", true)) { Info("Nothing was changed."); return 0; }

            a.Save(AnswersFile);
            var log = Path.Combine(WorkRoot, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var code = RunInstall(package, a, log);
            Console.WriteLine();
            if (code != 0)
            {
                Error($"Setup stopped. The log is {log}. Fix the cause and run setup again; it is safe to repeat.");
                return code;
            }
            Title("Done");
            Console.WriteLine($"  Open {a.Url} and sign in{(string.IsNullOrEmpty(a.AdminUser) ? "" : " as " + a.AdminUser)}.");
            Console.WriteLine("  Next: set up two-step verification, then System administration > NAV connection.");
            if (a.GeneratedDbPassword)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Database password for '{a.DbUser}' (store it in your password safe now; backups and updates need it):");
                Console.WriteLine($"    {a.DbPassword}");
                Console.ResetColor();
            }
            Console.WriteLine("  Backups: see deployment-windows-iis.md section 3, step 6, in " + package);
            Console.WriteLine($"  Log: {log}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Info("Cancelled. Nothing was changed.");
            return 1;
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return 1;
        }
        finally
        {
            if (!args.Contains("--no-pause", StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.Write("Press Enter to close.");
                Console.ReadLine();
            }
        }
    }

    // ------------------------------------------------------------------ payload

    private static string Unpack(string version)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                            ?? throw new InvalidOperationException("This setup file is damaged: the portal package is missing. Download it again.");
        var target = Path.Combine(WorkRoot, "package-" + string.Concat(version.Split(Path.GetInvalidFileNameChars())));
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        System.IO.Compression.ZipFile.ExtractToDirectory(payload, target);
        if (!File.Exists(Path.Combine(target, "Install-EssPortal.ps1")) || !File.Exists(Path.Combine(target, "app", "Ess.Api.dll")))
            throw new InvalidOperationException("The embedded package is incomplete.");
        Info($"Portal files unpacked to {target}");
        return target;
    }

    // ------------------------------------------------------------------ questions

    private static Answers Ask(Answers p, bool existing)
    {
        var a = new Answers();
        Title("1. Portal");
        a.Environment = Choose("Is this the live (Production) or test (UAT) server?", new[] { "Production", "UAT" }, p.Environment ?? "Production");
        a.HostName = Text("Portal host name (for example ess.company.co.ke)", p.HostName, required: true)!;
        Console.WriteLine("  The standard HTTPS port is 443. Use another port, such as 4048, if 443 is taken on this server.");
        a.HttpsPort = Number("Website HTTPS port", 1, 65535, p.HttpsPort == 0 ? 443 : p.HttpsPort);
        a.InstallRoot = Text("Install folder", p.InstallRoot ?? (a.Environment == "UAT" ? @"C:\inetpub\ess-portal-uat" : DefaultInstallRoot))!;
        a.DataRoot = Text("Data folder (documents, keys, logs, backups)", p.DataRoot ?? (a.Environment == "UAT" ? @"C:\EssDataUat" : @"C:\EssData"))!;

        Title("2. HTTPS certificate");
        var certs = ServerCertificates();
        for (var i = 0; i < certs.Count; i++)
            Console.WriteLine($"  {i + 1}. {certs[i].GetNameInfo(X509NameType.SimpleName, false),-40} expires {certs[i].NotAfter:yyyy-MM-dd}  {certs[i].Thumbprint}");
        Console.WriteLine($"  {certs.Count + 1}. Create a self-signed certificate for {a.HostName} (testing only; browsers will warn)");
        var defaultCert = certs.FindIndex(c => c.Thumbprint == p.CertificateThumbprint);
        var pick = Number("Choose a certificate", 1, certs.Count + 1,
            defaultCert >= 0 ? defaultCert + 1 : certs.FindIndex(c => MatchesHost(c, a.HostName)) is var m and >= 0 ? m + 1 : certs.Count + 1);
        if (pick == certs.Count + 1) a.CreateSelfSigned = true;
        else a.CertificateThumbprint = certs[pick - 1].Thumbprint;
        if (certs.Count > 0 && Confirm("Protect the encryption keys with a certificate, so they can be restored on another server? (recommended)", !string.IsNullOrEmpty(p.KeyProtectionThumbprint)))
        {
            var k = Number("Certificate number for key protection", 1, certs.Count,
                Math.Max(1, certs.FindIndex(c => c.Thumbprint == p.KeyProtectionThumbprint) + 1));
            a.KeyProtectionThumbprint = certs[k - 1].Thumbprint;
        }

        Title("3. Database (PostgreSQL 16)");
        Console.WriteLine("  This is the database server, not the website. PostgreSQL normally listens on port 5432.");
        while (true)
        {
            a.DbServer = Text("PostgreSQL server", p.DbServer ?? "localhost")!;
            // An earlier run may have saved the website's port here by mistake; offer the standard one instead.
            var defPort = p.DbPort == 0 || p.DbPort == a.HttpsPort ? 5432 : p.DbPort;
            a.DbPort = Number("PostgreSQL port (normally 5432)", 1, 65535, defPort);
            if (a.DbPort == a.HttpsPort) { Error($"  {a.DbPort} is the website's HTTPS port. Enter PostgreSQL's port (normally 5432)."); continue; }
            if (CanConnect(a.DbServer, a.DbPort)) break;
            Error($"  Nothing answers on {a.DbServer}:{a.DbPort}. Check that PostgreSQL is installed and running (Services: postgresql-x64-16) and that the port is right.");
            if (!Confirm("Try again?", true)) throw new OperationCanceledException();
        }
        a.DbName = Text("Database name", p.DbName ?? (a.Environment == "UAT" ? "ess_uat" : "ess"))!;
        a.DbUser = Text("Portal database user", p.DbUser ?? "ess_app")!;
        a.DbAdminPassword = Secret("Password of the PostgreSQL 'postgres' administrator", required: true);
        a.DbPassword = Secret(existing ? $"Password of '{a.DbUser}'" : $"New password for '{a.DbUser}' (Enter to generate one)", required: existing);
        if (string.IsNullOrEmpty(a.DbPassword))
        {
            a.DbPassword = GeneratePassword();
            a.GeneratedDbPassword = true;
        }
        else if (a.DbPassword.Length < 16) throw new InvalidOperationException("The database password must have at least 16 characters.");

        Title("4. Dynamics NAV 2016");
        Console.WriteLine("  If NAV uses Windows authentication, the portal must run as a domain account that has a NAV user.");
        if (Confirm("Does NAV use Windows authentication?", !string.IsNullOrEmpty(p.ServiceAccount)))
        {
            a.ServiceAccount = Text(@"Service account (DOMAIN\user)", p.ServiceAccount, required: true);
            a.ServiceAccountPassword = Secret("Service account password", required: true);
        }

        Title("5. E-mail and SMS (optional, can be added later by running setup again)");
        if (Confirm("Configure e-mail (SMTP) now?", !string.IsNullOrEmpty(p.SmtpHost)))
        {
            a.SmtpHost = Text("SMTP server", p.SmtpHost, required: true);
            a.SmtpPort = Number("SMTP port", 1, 65535, p.SmtpPort == 0 ? 587 : p.SmtpPort);
            a.SmtpUser = Text("SMTP user name (Enter for none)", p.SmtpUser);
            if (!string.IsNullOrEmpty(a.SmtpUser)) a.SmtpPassword = Secret("SMTP password", required: true);
            a.SmtpFrom = Text("Send e-mails from", p.SmtpFrom ?? a.SmtpUser, required: true);
        }
        if (Confirm("Configure SMS through Mobile Sasa now?", p.SmsProvider == "MobileSasa"))
        {
            a.SmsProvider = "MobileSasa";
            a.SmsApiKey = Secret("Mobile Sasa API token", required: true);
            a.SmsSenderId = Text("Sender ID", p.SmsSenderId, required: true);
        }

        Title("6. First administrator");
        if (!existing || Confirm("Create a first administrator? (only works while the portal has no users)", false))
        {
            a.AdminUser = Text("Administrator user name", "admin", required: true);
            while (true)
            {
                a.AdminPassword = Secret("Administrator password (12+ characters, mixed case, a digit and a symbol)", required: true);
                if (Secret("Type it again", required: true) == a.AdminPassword) break;
                Error("The passwords did not match.");
            }
        }
        return a;
    }

    private static bool CanConnect(string host, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            return client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(5)) && client.Connected;
        }
        catch (Exception) { return false; }
    }

    private static List<X509Certificate2> ServerCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Where(c => c.HasPrivateKey && c.NotAfter > DateTime.Now).OrderByDescending(c => c.NotAfter).ToList();
    }

    private static bool MatchesHost(X509Certificate2 c, string host)
    {
        var name = c.GetNameInfo(X509NameType.DnsName, false);
        return name.Equals(host, StringComparison.OrdinalIgnoreCase)
               || (name.StartsWith("*.") && host.EndsWith(name[1..], StringComparison.OrdinalIgnoreCase));
    }

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 24).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
    }

    // ------------------------------------------------------------------ install

    private static int RunInstall(string package, Answers a, string log)
    {
        var driver = Path.Combine(package, "Run-Setup.ps1");
        if (!File.Exists(driver)) throw new InvalidOperationException("The embedded package has no Run-Setup.ps1.");
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{driver}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = package,
        };
        // Empty values are left out: Windows treats an empty environment variable as absent, and the script reads a
        // missing key as $null anyway.
        void Set(string k, string? v)
        {
            if (string.IsNullOrEmpty(v)) psi.Environment.Remove("ESS_SETUP_" + k);
            else psi.Environment["ESS_SETUP_" + k] = v;
        }
        Set("LOG", log);
        Set("HOSTING_BUNDLE_URL", HostingBundleUrl);
        Set("ENVIRONMENT", a.Environment);
        Set("HOST", a.HostName);
        Set("HTTPS_PORT", a.HttpsPort.ToString());
        Set("INSTALL_ROOT", a.InstallRoot);
        Set("DATA_ROOT", a.DataRoot);
        Set("SITE", a.Environment == "UAT" ? "ESS Portal UAT" : "ESS Portal");
        Set("POOL", a.Environment == "UAT" ? "EssPortalUat" : "EssPortal");
        Set("CERT", a.CertificateThumbprint);
        Set("SELF_SIGNED", a.CreateSelfSigned ? "1" : "");
        Set("KEY_CERT", a.KeyProtectionThumbprint);
        Set("DB_SERVER", a.DbServer);
        Set("DB_PORT", a.DbPort.ToString());
        Set("DB_NAME", a.DbName);
        Set("DB_USER", a.DbUser);
        Set("DB_ADMIN_PASSWORD", a.DbAdminPassword);
        Set("DB_PASSWORD", a.DbPassword);
        Set("SERVICE_ACCOUNT", a.ServiceAccount);
        Set("SERVICE_PASSWORD", a.ServiceAccountPassword);
        Set("SMTP_HOST", a.SmtpHost);
        Set("SMTP_PORT", a.SmtpPort.ToString());
        Set("SMTP_USER", a.SmtpUser);
        Set("SMTP_PASSWORD", a.SmtpPassword);
        Set("SMTP_FROM", a.SmtpFrom);
        Set("SMS_PROVIDER", a.SmsProvider ?? "None");
        Set("SMS_KEY", a.SmsApiKey);
        Set("SMS_SENDER", a.SmsSenderId);
        Set("ADMIN_USER", a.AdminUser);
        Set("ADMIN_PASSWORD", a.AdminPassword);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
        p.WaitForExit();
        return p.ExitCode;
    }


    // ------------------------------------------------------------------ console helpers

    private static void Title(string t)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(t);
        Console.WriteLine(new string('-', t.Length));
        Console.ResetColor();
    }

    private static void Info(string m) => Console.WriteLine(m);

    private static void Error(string m)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(m);
        Console.ResetColor();
    }

    private static string ReadLine() => Console.ReadLine() ?? throw new OperationCanceledException();

    private static string? Text(string prompt, string? def, bool required = false)
    {
        while (true)
        {
            Console.Write(string.IsNullOrEmpty(def) ? $"  {prompt}: " : $"  {prompt} [{def}]: ");
            var v = ReadLine().Trim();
            if (v.Length == 0) v = def ?? "";
            if (v.Length > 0 || !required) return v.Length == 0 ? null : v;
            Error("  A value is needed.");
        }
    }

    private static int Number(string prompt, int min, int max, int def)
    {
        while (true)
        {
            var v = Text(prompt, def.ToString());
            if (int.TryParse(v, out var n) && n >= min && n <= max) return n;
            Error($"  Enter a number from {min} to {max}.");
        }
    }

    private static string Choose(string prompt, string[] options, string def)
    {
        Console.WriteLine($"  {prompt}");
        for (var i = 0; i < options.Length; i++) Console.WriteLine($"    {i + 1}. {options[i]}");
        return options[Number("Choose", 1, options.Length, Array.IndexOf(options, def) + 1) - 1];
    }

    private static bool Confirm(string prompt, bool def)
    {
        while (true)
        {
            Console.Write($"  {prompt} [{(def ? "Y/n" : "y/N")}]: ");
            var v = ReadLine().Trim().ToLowerInvariant();
            if (v.Length == 0) return def;
            if (v is "y" or "yes") return true;
            if (v is "n" or "no") return false;
        }
    }

    private static string Secret(string prompt, bool required)
    {
        while (true)
        {
            Console.Write($"  {prompt}: ");
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); } continue; }
                if (k.Key == ConsoleKey.Escape) throw new OperationCanceledException();
                if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); }
            }
            Console.WriteLine();
            if (sb.Length > 0 || !required) return sb.ToString();
            Error("  A value is needed.");
        }
    }
}

/// <summary>The answers. Only the non-secret ones are saved, as defaults for the next run.</summary>
internal sealed class Answers
{
    public string? Environment { get; set; }
    public string? HostName { get; set; }
    public int HttpsPort { get; set; }
    public string? InstallRoot { get; set; }
    public string? DataRoot { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? KeyProtectionThumbprint { get; set; }
    public string? DbServer { get; set; }
    public int DbPort { get; set; }
    public string? DbName { get; set; }
    public string? DbUser { get; set; }
    public string? ServiceAccount { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpFrom { get; set; }
    public string? SmsProvider { get; set; }
    public string? SmsSenderId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string Url => HttpsPort is 0 or 443 ? $"https://{HostName}" : $"https://{HostName}:{HttpsPort}";
    [System.Text.Json.Serialization.JsonIgnore] public bool CreateSelfSigned { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? DbAdminPassword { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? DbPassword { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool GeneratedDbPassword { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? ServiceAccountPassword { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? SmtpPassword { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? SmsApiKey { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? AdminUser { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? AdminPassword { get; set; }

    public static Answers Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Answers>(File.ReadAllText(path)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(string path)
    {
        // Saved after the self-signed certificate choice is known only as "create"; the thumbprint is filled on the next run's list.
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
