<?php

declare(strict_types=1);

/*
 * Inkspire Control Panel helper (embedded in InkspirePanel.exe, written to data\tmp and deleted after the run).
 *
 * Seeds Inkspire's reference data exactly once and creates/updates the first administrator.
 * It never calls DatabaseSeeder: that one creates a demo admin with a published password.
 *
 * Input (environment, so the password never appears on a command line):
 *   INKSPIRE_APP_DIR  INKSPIRE_ADMIN_NAME  INKSPIRE_ADMIN_EMAIL  INKSPIRE_ADMIN_PASSWORD
 * Output: "PROVISION_OK" on success; a message on stderr and a non-zero exit code otherwise.
 */

use App\Enums\UserRole;
use App\Models\SystemConfiguration;
use App\Models\User;
use Illuminate\Contracts\Console\Kernel;
use Illuminate\Support\Facades\Artisan;

$appDir = rtrim((string) getenv('INKSPIRE_APP_DIR'), '\\/');
$name = (string) getenv('INKSPIRE_ADMIN_NAME');
$email = (string) getenv('INKSPIRE_ADMIN_EMAIL');
$password = (string) getenv('INKSPIRE_ADMIN_PASSWORD');

if ($appDir === '' || $name === '' || $email === '' || $password === '') {
    fwrite(STDERR, "Missing INKSPIRE_* environment variables.\n");
    exit(2);
}

require $appDir.'/vendor/autoload.php';
$app = require $appDir.'/bootstrap/app.php';
$app->make(Kernel::class)->bootstrap();

// Reference data: price list, print sizes, business rules. Their seeders overwrite values on every run, so only
// ever run them on an empty install.
if (SystemConfiguration::query()->count() === 0) {
    foreach (['SystemConfigurationSeeder', 'PricingDatabaseSeeder', 'SpecificationOptionSeeder'] as $seeder) {
        $code = Artisan::call('db:seed', ['--class' => 'Database\\Seeders\\'.$seeder, '--force' => true]);
        echo Artisan::output();
        if ($code !== 0) {
            fwrite(STDERR, "Seeder {$seeder} failed.\n");
            exit(3);
        }
    }
}

// Same shape as DatabaseSeeder::createAdministrator(), with the owner's own credentials. Events are off so the
// audit observer does not need a request context.
User::withoutEvents(function () use ($name, $email, $password): void {
    $admin = User::query()->updateOrCreate(
        ['email' => $email],
        [
            'name' => $name,
            'password' => $password,
            'role' => UserRole::Admin->value,
            'email_verified_at' => now(),
        ],
    );
    $admin->forceFill(['is_active' => true])->save();
});

echo "PROVISION_OK\n";
