const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const expected = {
  packageVersion: '0.2.0-beta.1',
  assemblyVersion: '0.2.0.0',
  targetFramework: 'net472',
  runtimeIdentifier: 'win-x64',
  hdtBaselineVersion: '1.53.5.0',
};

const errors = [];

function readText(relativePath) {
  const fullPath = path.join(root, relativePath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`missing ${relativePath}`);
    return '';
  }
  return fs.readFileSync(fullPath, 'utf8');
}

function readJson(relativePath) {
  const text = readText(relativePath);
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch (error) {
    errors.push(`invalid JSON ${relativePath}: ${error.message}`);
    return null;
  }
}

function requireEqual(actual, wanted, label) {
  if (actual !== wanted) {
    errors.push(`${label}: expected ${wanted}, got ${String(actual)}`);
  }
}

const identity = readJson('release_identity.json');
if (identity) {
  for (const [key, value] of Object.entries(expected)) {
    requireEqual(identity[key], value, `release_identity.${key}`);
  }
  requireEqual(Object.keys(identity).sort().join(','), Object.keys(expected).sort().join(','), 'release_identity fields');
}

const packageJson = readJson('package.json');
if (packageJson) {
  requireEqual(packageJson.version, expected.packageVersion, 'package.json version');
}

if (fs.existsSync(path.join(root, 'package-lock.json'))) {
  errors.push('package-lock.json must not exist in the dependency-free repository');
}

const assemblyInfo = readText('src/BobCoach/ReleaseAssemblyInfo.cs');
if (assemblyInfo) {
  const escapedAssembly = expected.assemblyVersion.replace(/\./g, '\\.');
  const escapedPackage = expected.packageVersion.replace(/\./g, '\\.');
  if (!new RegExp(`AssemblyVersion\\("${escapedAssembly}"\\)`).test(assemblyInfo)) {
    errors.push('ReleaseAssemblyInfo.cs AssemblyVersion mismatch');
  }
  if (!new RegExp(`AssemblyFileVersion\\("${escapedAssembly}"\\)`).test(assemblyInfo)) {
    errors.push('ReleaseAssemblyInfo.cs AssemblyFileVersion mismatch');
  }
  if (!new RegExp(`AssemblyInformationalVersion\\("${escapedPackage}"\\)`).test(assemblyInfo)) {
    errors.push('ReleaseAssemblyInfo.cs AssemblyInformationalVersion mismatch');
  }
}

const pluginSource = readText('src/BobCoach/BobCoachPlugin.cs');
if (pluginSource) {
  if (!/public\s+Version\s+Version\s*\{\s*get\s*\{\s*return\s+Assembly\.GetExecutingAssembly\(\)\.GetName\(\)\.Version;\s*\}\s*\}/s.test(pluginSource)) {
    errors.push('BobCoachPlugin.Version must derive from the executing assembly version');
  }
  if (/public\s+Version\s+Version[\s\S]{0,160}new\s+Version\s*\(/.test(pluginSource)) {
    errors.push('BobCoachPlugin.Version retains an independent numeric literal');
  }
}

if (errors.length > 0) {
  console.error('FAIL release identity contract');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log('PASS release identity package=0.2.0-beta.1 assembly=0.2.0.0 framework=net472 rid=win-x64 hdt=1.53.5.0');
