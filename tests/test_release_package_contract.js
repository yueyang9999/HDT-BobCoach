'use strict';

const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const releaseRoot = path.join(root, 'tools', 'release');
const errors = [];
const expectedFiles = [
  'BobCoach.dll',
  'README_OFFLINE.md',
  'INSTALL.ps1',
  'UNINSTALL.ps1',
  'LICENSE',
  'NOTICE',
  'DATA_SOURCES.md',
  'PRIVACY.md',
  'SUPPORT.md',
  'manifest.json',
  'SHA256SUMS.txt',
];

function read(relativePath) {
  const fullPath = path.join(root, relativePath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`missing ${relativePath}`);
    return '';
  }
  return fs.readFileSync(fullPath, 'utf8');
}

function requireText(source, token, label) {
  if (!source.includes(token)) errors.push(`missing ${label}`);
}

function forbid(source, pattern, label) {
  if (pattern.test(source)) errors.push(`forbidden ${label}`);
}

function packageFiles(source, label) {
  const match = source.match(/\$PackageFiles\s*=\s*@\(([\s\S]*?)\)\s*(?:\r?\n|$)/);
  if (!match) {
    errors.push(`missing ${label} PackageFiles array`);
    return [];
  }
  return [...match[1].matchAll(/"([^"]+)"/g)].map((entry) => entry[1]);
}

const installer = read('tools/release/INSTALL.ps1');
const uninstaller = read('tools/release/UNINSTALL.ps1');
const builder = read('tools/release/build_offline_package.ps1');
const lifecycle = read('tools/release/verify_offline_package_lifecycle.ps1');
const lifecycleConsumerTest = read('tests/test_offline_package_lifecycle_consumer.ps1');
const readme = read('tools/release/README_OFFLINE.md');

for (const [source, label] of [[installer, 'installer'], [builder, 'builder']]) {
  if (JSON.stringify(packageFiles(source, label)) !== JSON.stringify(expectedFiles)) {
    errors.push(`${label} package whitelist mismatch`);
  }
}

for (const [token, label] of [
  ['SHA256SUMS.txt', 'installer hash gate'],
  ['AssemblyFileVersionAttribute', 'installer file-version gate'],
  ['AssemblyInformationalVersionAttribute', 'installer informational-version gate'],
  ['PE32Plus', 'installer PE32Plus gate'],
  ['ImageFileMachine]::AMD64', 'installer AMD64 gate'],
  ['[IO.File]::Replace', 'installer atomic replace'],
  ['-Rollback', 'installer rollback mode'],
  ['BackupPath', 'installer selected backup'],
  ['"Hearthstone Deck Tracker"', 'installer official HDT process'],
  ['"HearthstoneDeckTracker"', 'installer legacy HDT process'],
]) requireText(installer, token, label);

for (const [token, label] of [
  ['SupportsShouldProcess', 'uninstaller confirmation gate'],
  ['RemoveUserData', 'uninstaller explicit user-data switch'],
  ['ReparsePoint', 'uninstaller reparse-point gate'],
  ['"BobCoach.dll"', 'uninstaller exact DLL target'],
]) requireText(uninstaller, token, label);

for (const [token, label] of [
  ['tools\\build\\build_release.ps1', 'builder release-core path'],
  ['bobcoach-offline-package-', 'builder unique temporary root'],
  ['CreateEntry', 'builder explicit ZIP entries'],
  ['LastWriteTime', 'builder fixed ZIP entry timestamps'],
  ['manifest.json', 'builder manifest'],
  ['SHA256SUMS.txt', 'builder package hashes'],
  ['$zipPath.sha256', 'builder external ZIP hash'],
  ['Get-PluginFacts $stagedPluginPath', 'builder staged DLL validation'],
  ['CurrentSeasonPreview is retained only for historical 0.2.0-beta.1 artifacts', 'builder retired preview gate'],
]) requireText(builder, token, label);

forbid(installer, /Invoke-WebRequest|Start-BitsTransfer|WebClient|HttpClient/i, 'installer network access');
forbid(installer, /New-ItemProperty|Set-ItemProperty|reg\.exe/i, 'installer registry write');
forbid(installer, /Remove-Item[^\r\n]+Plugins[^\r\n]+-Recurse/i, 'installer recursive Plugins deletion');
forbid(uninstaller, /Remove-Item[^\r\n]+Plugins[^\r\n]+-Recurse/i, 'uninstaller recursive Plugins deletion');
forbid(builder, /Copy-Item[^\r\n]*\*[^\r\n]*-Recurse/i, 'builder recursive wildcard copy');
forbid(builder, /HearthstoneDeckTracker\\Plugins|AppData\\Roaming/i, 'builder deployment path');

for (const phase of ['Install', 'Upgrade', 'Rollback', 'Uninstall', 'Reinstall']) {
  if (!new RegExp(`Invoke-LifecycleStep[^\\r\\n]+\\"${phase}\\"`).test(lifecycle)) {
    errors.push(`missing lifecycle ${phase.toLowerCase()} phase`);
  }
}
requireText(
  lifecycleConsumerTest,
  '[int]$CommandTimeoutSeconds = 120',
  'lifecycle consumer production-aligned default timeout',
);
for (const token of ['0.2.0-beta.2', 'Get-FileHash', '-Rollback', '-RemoveUserData', 'Windows 10 22H2']) {
  requireText(readme, token, `offline README ${token}`);
}

const packageJson = JSON.parse(read('package.json') || '{}');
const packageScript = packageJson.scripts && packageJson.scripts['test:package'];
if (!packageScript || !packageScript.includes('run_package_tests.ps1')) {
  errors.push('package.json test:package must invoke tests/run_package_tests.ps1');
}
if (!packageJson.scripts || !packageJson.scripts.test || !packageJson.scripts.test.includes('test:package')) {
  errors.push('default npm test omits test:package');
}

if (errors.length) {
  console.error('FAIL offline package static contract');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`PASS offline package static whitelist=${expectedFiles.length} lifecycle=install-upgrade-rollback-uninstall-reinstall`);
