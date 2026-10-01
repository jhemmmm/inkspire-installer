using Inkspire.Panel.App;

namespace Inkspire.Panel.Setup;

/// <summary>Decides which payloads this install needs. Used by the Download page and the headless dev install. With a
/// <c>localDir</c> (offline install / tests) the files found there are used instead of the internet.</summary>
public static class PayloadPlan
{
    public static async Task<IReadOnlyList<Payload>> BuildAsync(WizardState s, HttpClient http, CancellationToken ct, string? localDir = null)
    {
        var paths = new AppPaths(s.InstallRoot);
        var list = new List<Payload>
        {
            FromDir(localDir, "php-*-nts-Win32-vs17-x64.zip", "php", "PHP", PayloadKind.Zip, stripTopLevel: false)
                ?? await PayloadCatalog.ResolvePhpAsync(http, ct),
            FromDir(localDir, "nginx-*.zip", "nginx", "nginx", PayloadKind.Zip, stripTopLevel: true)
                ?? await PayloadCatalog.ResolveNginxAsync(http, ct),
            FromDir(localDir, "cacert.pem", "cacert", "CA certificate bundle", PayloadKind.File, stripTopLevel: false) ?? PayloadCatalog.CaBundle,
        };

        s.NeedVcRedist = !PrereqChecker.IsVcRedist2015PlusInstalled();
        if (s.NeedVcRedist) list.Add(FromDir(localDir, "vc_redist.x64.exe", "vcredist", "Visual C++ runtime", PayloadKind.Exe, false) ?? PayloadCatalog.VcRedist);

        // A repair that already has working app code does not download it again.
        if (!(s.IsRepair && File.Exists(paths.AppArtisan)))
        {
            if (s.LocalAppZip is { } zip)
                list.Add(PayloadCatalog.LocalApp(zip, Path.GetFileNameWithoutExtension(zip)));
            else if (FromDir(localDir, "inkspire-app-*.zip", "app", "Inkspire", PayloadKind.Zip, stripTopLevel: true) is { } fromDir)
                list.Add(fromDir);
            else
                list.Add(await PayloadCatalog.ResolveAppAsync(http, ct));
        }
        return list;
    }

    private static Payload? FromDir(string? dir, string pattern, string id, string title, PayloadKind kind, bool stripTopLevel)
    {
        if (dir is null || !Directory.Exists(dir)) return null;
        var file = Directory.GetFiles(dir, pattern).OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (file is null) return null;
        var name = Path.GetFileName(file);
        // The version is whatever sits between the first dash and the extension ("php-8.4.12-nts-..." -> 8.4.12).
        var version = id switch
        {
            "php" => name.Split('-').ElementAtOrDefault(1) ?? "local",
            "nginx" => Path.GetFileNameWithoutExtension(name).Replace("nginx-", ""),
            "app" => Path.GetFileNameWithoutExtension(name).Replace("inkspire-app-", ""),
            _ => "local",
        };
        return new Payload(id, $"{title} ({name})", new Uri(Path.GetFullPath(file)).AbsoluteUri, name, kind, version, stripTopLevel);
    }
}
