// 通用版本 bump 工具: node tools/release/bump_version.cjs <新版本如 1.2.23>
// 从 release_identity.json 读取当前版本, 全链路替换为指定新版本(自动含所有版本契约触点)。
const fs = require('fs');
const path = require('path');
const repoRoot = path.resolve(__dirname, '..', '..');
const files = [
  'package.json','release_identity.json','NOTICE','tools/release/verify_offline_package_lifecycle.ps1',
  'tools/release/build_offline_package.ps1','README.md','tools/release/INSTALL.ps1',
  'src/BobCoach/ReleaseAssemblyInfo.cs','README.en.md','tools/release/README_OFFLINE.md','CHANGELOG.md',
  'docs/user/INSTALL.md','docs/user/INSTALL.html',
  'tests/test_public_documentation_contract.js','tests/test_release_identity.js',
  'tests/test_release_package_contract.js','tests/test_offline_package_lifecycle_consumer.ps1',
  'tests/test_install_guide_contract.js','tests/test_offline_package_builder.ps1'];
const newVersion = process.argv[2];
if (!newVersion || !/^\d+\.\d+\.\d+$/.test(newVersion)) {
  console.error('usage: node tools/release/bump_version.cjs <new-version>');
  process.exit(1);
}
const identityPath = path.join(repoRoot, 'release_identity.json');
const identity = JSON.parse(fs.readFileSync(identityPath, 'utf8'));
const oldVersion = identity.packageVersion;
if (oldVersion === newVersion) { console.error('already at ' + newVersion); process.exit(1); }
console.log('bump ' + oldVersion + ' -> ' + newVersion);
let total = 0;
for (const rel of files) {
  const p = path.join(repoRoot, rel);
  if (!fs.existsSync(p)) { console.warn('SKIP missing: ' + rel); continue; }
  let t = fs.readFileSync(p, 'utf8');
  if (t.charCodeAt(0) === 0xFEFF) t = t.slice(1);
  const oldAsm = oldVersion + '.0';
  const newAsm = newVersion + '.0';
  let n = 0;
  let out = t.split(oldAsm).join(newAsm); n += (t.split(oldAsm).length - 1);
  out = out.split(oldVersion).join(newVersion); n += (t.split(oldVersion).length - 1) - (t.split(oldAsm).length - 1);
  if (n > 0) { fs.writeFileSync(p, out, 'utf8'); total += n; console.log(rel + ': ' + n); }
}
console.log('total replaced', total);