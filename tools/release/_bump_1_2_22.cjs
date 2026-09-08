const fs = require('fs');
const files = [
  'package.json','release_identity.json','NOTICE','tools/release/verify_offline_package_lifecycle.ps1',
  'tools/release/build_offline_package.ps1','README.md','tools/release/INSTALL.ps1',
  'src/BobCoach/ReleaseAssemblyInfo.cs','README.en.md','tools/release/README_OFFLINE.md','CHANGELOG.md',
  'docs/user/INSTALL.md','docs/user/INSTALL.html',
  'tests/test_public_documentation_contract.js','tests/test_offline_package_lifecycle_consumer.ps1',
  'tests/test_install_guide_contract.js','tests/test_offline_package_builder.ps1'];
let total = 0;
for (const f of files) {
  const p = 'E:/HDT-BobCoach/' + f;
  let t = fs.readFileSync(p, 'utf8');
  if (t.charCodeAt(0) === 0xFEFF) t = t.slice(1);
  let n = 0;
  const m = t.split('1.2.21').length - 1;
  if (m > 0) { t = t.split('1.2.21').join('1.2.22'); n = m; }
  if (n > 0) { fs.writeFileSync(p, '﻿' + t, 'utf8'); total += n; console.log(f + ': ' + n); }
}
console.log('total replaced', total);