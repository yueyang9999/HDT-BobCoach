'use strict';

const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const errors = [];
const expectedGuideImages = [
  'docs/user/images/install/install-01-exit-hdt.png',
  'docs/user/images/install/install-02-open-plugins-folder.png',
  'docs/user/images/install/install-03-copy-bobcoach-dll.png',
  'docs/user/images/install/install-04-enable-bobcoach.png',
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

const markdown = read('docs/user/INSTALL.md');
const html = read('docs/user/INSTALL.html');
const chineseReadme = read('README.md');
const englishReadme = read('README.en.md');
const offlineReadme = read('tools/release/README_OFFLINE.md');

for (const imagePath of expectedGuideImages) {
  if (!fs.existsSync(path.join(root, imagePath))) errors.push(`missing ${imagePath}`);
  const markdownPath = imagePath.replace(/^docs\/user\//, '');
  const markdownImage = new RegExp(`!\\[[^\\]]+\\]\\(${markdownPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\)`);
  if (!markdownImage.test(markdown)) errors.push(`missing Markdown image with alt text ${markdownPath}`);

  const htmlPath = imagePath.replace(/^docs\/user\//, '');
  const htmlImage = new RegExp(`<img[^>]+src=["']${htmlPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}["'][^>]+alt=["'][^"']+["']`, 'i');
  if (!htmlImage.test(html)) errors.push(`missing HTML image with alt text ${htmlPath}`);
}

for (const [source, label, tokens] of [
  [markdown, 'Markdown guide', ['完全退出 HDT', '%AppData%\\HearthstoneDeckTracker\\Plugins', 'BobCoach.dll', '根目录', '启用 BobCoach', '可选高级安装']],
  [html, 'HTML guide', ['完全退出 HDT', '%AppData%\\HearthstoneDeckTracker\\Plugins', 'BobCoach.dll', '根目录', '启用 BobCoach', '可选高级安装']],
  [chineseReadme, 'Chinese README', ['复制 `BobCoach.dll`', '%AppData%\\HearthstoneDeckTracker\\Plugins', '启用 BobCoach', 'docs/user/INSTALL.html']],
  [englishReadme, 'English README', ['copy `BobCoach.dll`', '%AppData%\\HearthstoneDeckTracker\\Plugins', 'enable BobCoach', 'optional advanced']],
  [offlineReadme, 'offline README', ['安装教程.html', '复制 `BobCoach.dll`', '%APPDATA%\\HearthstoneDeckTracker\\Plugins', '可选高级安装']],
]) {
  for (const token of tokens) requireText(source, token, `${label}: ${token}`);
}

requireText(markdown, '[下载或离线打开 HTML 图文教程](INSTALL.html)', 'Markdown link to HTML guide');
requireText(html, 'overflow-wrap: anywhere', 'mobile wrapping for long paths');
forbid(html, /<(?:img|script|link|source)\b[^>]+(?:src|href)=["'](?:https?:)?\/\//i, 'remote HTML resource');
forbid(html, /<script\b/i, 'HTML script');
forbid(chineseReadme, /仍需[^。\n]*运行 `INSTALL\.ps1`|必须[^。\n]*运行 `INSTALL\.ps1`/, 'mandatory PowerShell in Chinese README');
forbid(englishReadme, /not complete until `INSTALL\.ps1` has been run/i, 'mandatory PowerShell in English README');
forbid(offlineReadme, /## 默认安装或升级[\s\S]{0,800}powershell/i, 'PowerShell in default offline install flow');

if (errors.length) {
  console.error('FAIL beginner-friendly install guide contract');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`PASS beginner-friendly install guide images=${expectedGuideImages.length} offline-html=local-only`);
