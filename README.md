# Inkspire Control Panel

One Windows program (`InkspirePanel.exe`) that

* **installs** [Inkspire](https://github.com/jhemmmm/Inkspire) on one PC of the shop: PHP 8.4, nginx, the app, a SQLite database, a first administrator;
* then sits in the notification area as a **XAMPP-style control panel**: start / stop / restart each service, live logs, backups, updates, settings;
* **opens Windows Firewall** so the other PCs on the shop network can use Inkspire from their browser. Nothing is installed on those PCs.

```
 Other PCs (browser only) ──http://192.168.1.50:8080──►  SERVER PC
                                                          InkspirePanel.exe  (tray, your Windows user)
                                                            ├─ nginx            0.0.0.0:8080
                                                            ├─ php-cgi × 6      127.0.0.1:9001-9006
                                                            └─ php artisan schedule:work
                                                          data\inkspire.sqlite  (WAL)   data\storage (uploads)
```

## For the shop

### Install (once, on the server PC)

1. Download `InkspirePanel.exe` from the [Releases](https://github.com/jhemmmm/inkspire-installer/releases) page and run it.
   (Windows SmartScreen may warn about an unknown publisher: *More info → Run anyway*. The file is not code-signed.)
2. Click through the wizard. It checks the PC, asks for the install folder (default `C:\Inkspire`), the shop name, the
   administrator's email and password, and downloads about 130 MB.
3. Approve the **one** Windows administrator prompt. It opens the firewall port for the shop network and installs the
   Visual C++ runtime if missing.
4. Finish. The control panel starts Inkspire; sign in at `http://localhost:8080/`.

### On each of the other PCs

Open Chrome / Edge / Firefox and type the address the panel shows under **Network & clients**, for example
`http://192.168.1.50:8080/`. Nothing to install, no firewall changes. The panel can copy ready-made instructions and save
an `Inkspire.url` shortcut to hand out.

* Every person needs **their own Inkspire account** (Admin → Users). A second login on the same account signs the first one out.
* Artists need internet on their PC for the Photopea design editor.

### Keep it reliable

* The server PC must be **on and signed in to Windows** while the shop is open. The panel starts Inkspire at sign-in
  and keeps the PC from going to sleep. (Windows auto-sign-in is up to you; Windows Update restarts need someone to sign in again.)
* Give the server PC a **fixed address**: reserve it in the router (DHCP reservation). If it changes, the panel notices and
  *Network & clients → Fix* repairs Inkspire's own address.
* **Backups** run daily into `data\backups` (database + `.env`, which holds the encryption key). Copy that folder to a USB
  drive or another PC now and then: one PC is one point of failure.
* **Updates**: *Updates* checks GitHub; installing takes about a minute, makes a backup first, and puts the old version back
  automatically if anything fails.

### When another PC cannot connect

Open **Network & clients** on the server. It checks, in order: nginx listening for other PCs, the firewall rule (and any
"block" rule created when someone pressed *Cancel* on the Windows "Allow access" prompt), the network type, the PC's address
(and whether DHCP may change it), and whether the app answers on that address. **Fix problems** repairs what it can.
Still stuck?

* On the other PC (PowerShell): `Test-NetConnection <server address> -Port 8080`: `TcpTestSucceeded : False` means something
  between the two PCs blocks it: Wi-Fi "client isolation" (guest networks), a third-party antivirus firewall, or a different network/subnet
  (then choose *Any private address* in Settings).
* The router needs no changes and no port forwarding: Inkspire is reachable on the shop network only.

### Known limits (v1)

* Links that must work **outside** the shop (design-review emails to customers, QR tracking slips, PayMongo webhooks)
  use the server's shop-network address and only work on that network. Public access (Cloudflare Tunnel) is planned.
* LAN access is plain `http://`. Inkspire itself works over it (`crypto.randomUUID` needs the one-line fix in
  [`inkspire-repo/`](inkspire-repo/README.md)); HTTPS would need a certificate trusted on every PC.
* No Windows-service mode yet: the panel runs in your Windows session.

## Folder layout (`C:\Inkspire`)

```
panel\        InkspirePanel.exe
php\  nginx\  PHP 8.4 NTS, nginx (php.ini and nginx.conf are generated: change them in Settings)
app\          the Inkspire app (code only; replaced on update).  app\storage -> data\storage  (junction)
data\         inkspire.sqlite · storage\ (uploads, sessions, cache, logs) · backups\   ← everything precious
config\       panel.json (panel settings)         logs\  panel.log, php-errors.log, admin-task.log
downloads\    cached downloads (reused for an offline reinstall)
```

## Developer notes

```powershell
dotnet build                                # .NET 8 WinForms, flat project (inkspire-installer.csproj)
dotnet test tests\InkspirePanel.Tests       # config writers, env/ini patchers, process supervision, junctions, firewall read, ...
.\publish.ps1 -Version 1.0.0                # single-file self-contained dist\InkspirePanel.exe
.\release.ps1 -Version 1.0.0                # tests, tag v1.0.0, push: GitHub Actions publishes the exe
```

Headless harnesses (no UI, no UAC, no registration), used for end-to-end checks on a scratch folder:

```
InkspirePanel.exe --dev-install  <root> [--payloads <dir>] [--app-zip <bundle.zip>] [--admin-email x]
InkspirePanel.exe --dev-provision <root> [--admin-email x]     # config + .env + db + migrate on an extracted tree
InkspirePanel.exe --dev-smoke    <root> [holdSeconds]         # start all, fetch /up + /login, network diagnosis, stop, no leftovers
InkspirePanel.exe --dev-update   <root> <bundle.zip> [version] # the real updater, incl. rollback
InkspirePanel.exe --dev-firewall <port> [scope] [--keep]      # real firewall add/read/remove (raises UAC prompts)
InkspirePanel.exe --dev-screenshots <dir> [--install-root <root>]   # render wizard + panel windows to PNG
InkspirePanel.exe --wizard [--install-root <root>]            # open the setup wizard over an existing install
InkspirePanel.exe --uninstall
```

`--dev-install` needs `php-*-nts-Win32-vs17-x64.zip`, `nginx-*.zip`, `cacert.pem` and an `inkspire-app-*.zip` in `--payloads`.
The installer downloads from `windows.php.net`, `nginx.org`, `curl.se` and the latest GitHub release of `jhemmmm/Inkspire`
(asset `inkspire-app-<tag>.zip` + `.sha256`, one top-level folder; see `inkspire-repo/.github/workflows/release.yml`).

Design decisions worth knowing before changing things (each is covered by a test or a harness run):

* nginx must **not** answer 404 for static-looking paths outside `/build/`: Inkspire's private disk serves signed `/storage/...`
  URLs through Laravel. Only `index.php` runs PHP. `least_conn` + `max_fails=0` keep a recycling php-cgi worker from causing 502s.
* Seeders reset prices and business rules, so first-run data is created **once** (`PanelConfig.App.Seeded`); the demo admin of
  `DatabaseSeeder` is never created.
* `Directory.Delete(path, recursive: true)` throws on a folder containing a junction: use `FolderOps.DeleteTree`.
* An update swaps `app\` only; the pre-update database snapshot is restored if anything after the swap fails.
* php-cgi honours `PHP_FCGI_MAX_REQUESTS` on Windows and exits when it is reached; every exit leaves a ~0.1 s gap, so the default is high (10000).
