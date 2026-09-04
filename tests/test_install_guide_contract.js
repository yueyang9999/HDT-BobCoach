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
const expectedStatusStripImages = [
  'docs/user/images/features/status-strip-settings-entry.jpg',
  'docs/user/images/features/status-strip-controls.jpg',
  'docs/user/images/features/status-strip-result.jpg',
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
const featuresMarkdown = read('docs/user/FEATURES.md');
const featuresHtml = read('docs/user/FEATURES.html');
const chineseReadme = read('README.md');
const englishReadme = read('README.en.md');
const offlineReadme = read('tools/release/README_OFFLINE.md');
const stableAssetBase = 'https://github.com/yueyang9999/HDT-BobCoach/releases/download/v1.2.8/';

for (const imagePath of expectedGuideImages) {
  if (!fs.existsSync(path.join(root, imagePath))) errors.push(`missing ${imagePath}`);
  const markdownPath = imagePath.replace(/^docs\/user\//, '');
  const markdownImage = new RegExp(`!\\[[^\\]]+\\]\\(${markdownPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\)`);
  if (!markdownImage.test(markdown)) errors.push(`missing Markdown image with alt text ${markdownPath}`);

  const htmlPath = imagePath.replace(/^docs\/user\//, '');
  const htmlImage = new RegExp(`<img[^>]+src=["']${htmlPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}["'][^>]+alt=["'][^"']+["']`, 'i');
  if (!htmlImage.test(html)) errors.push(`missing HTML image with alt text ${htmlPath}`);
}

for (const imagePath of expectedStatusStripImages) {
  const fullPath = path.join(root, imagePath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`missing ${imagePath}`);
    continue;
  }
  const image = fs.readFileSync(fullPath);
  if (image.length > 512000 || image[0] !== 0xff || image[1] !== 0xd8 || !image.includes(Buffer.from([0xff, 0xc2]))) {
    errors.push(`invalid optimized progressive JPEG ${imagePath}`);
  }
  const relativePath = imagePath.replace(/^docs\/user\//, '');
  const escapedPath = relativePath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  if (!new RegExp(`!\\[[^\\]]+\\]\\(${escapedPath}\\)`).test(featuresMarkdown)) {
    errors.push(`missing Markdown status-strip image with alt text ${relativePath}`);
  }
  if (!new RegExp(`<img[^>]+src=["']${escapedPath}["'][^>]+alt=["'][^"']+["']`, 'i').test(featuresHtml)) {
    errors.push(`missing HTML status-strip image with alt text ${relativePath}`);
  }
}

for (const [source, label] of [
  [featuresMarkdown, 'Markdown status-strip guide'],
  [featuresHtml, 'HTML status-strip guide'],
]) {
  for (const token of ['选项 > 插件 > Bob教练设置', '字体大小', '解锁状态面板', 'Hearthstone 窗口', '鼠标穿透', '恢复默认']) {
    requireText(source, token, `${label}: ${token}`);
  }
}

for (const [source, label, tokens] of [
  [markdown, 'Markdown guide', ['完全退出 HDT', '打开插件文件夹', 'BobCoach.dll', 'BobCoach.dll.sha256', 'Plugins', '开启 BobCoach', '可选高级安装']],
  [html, 'HTML guide', ['完全退出 HDT', '打开插件文件夹', 'BobCoach.dll', 'BobCoach.dll.sha256', 'Plugins', '开启 BobCoach', '可选高级安装']],
  [chineseReadme, 'Chinese README', ['把 `BobCoach.dll` 和 `BobCoach.dll.sha256` 一起复制', '%AppData%\\HearthstoneDeckTracker\\Plugins', '启用 BobCoach', 'docs/user/INSTALL.html']],
  [englishReadme, 'English README', ['Copy both `BobCoach.dll` and `BobCoach.dll.sha256`', '%AppData%\\HearthstoneDeckTracker\\Plugins', 'enable BobCoach', 'optional advanced']],
  [offlineReadme, 'offline README', ['安装教程.html', '手动复制 `BobCoach.dll` 和相邻的 `BobCoach.dll.sha256`', '%APPDATA%\\HearthstoneDeckTracker\\Plugins', '可选高级安装']],
]) {
  for (const token of tokens) requireText(source, token, `${label}: ${token}`);
}

requireText(markdown, '[下载或离线打开 HTML 图文教程](INSTALL.html)', 'Markdown link to HTML guide');
for (const [source, label] of [
  [markdown, 'Markdown guide'],
  [html, 'HTML guide'],
  [chineseReadme, 'Chinese README'],
  [englishReadme, 'English README'],
]) {
  requireText(source, 'BobCoach-1.2.8-win-x64.zip', `${label}: current package name`);
  requireText(source, `${stableAssetBase}BobCoach-1.2.8-win-x64.zip`, `${label}: official installer link`);
  requireText(source, `${stableAssetBase}BobCoach-1.2.8-win-x64.zip.sha256`, `${label}: official checksum link`);
  forbid(source, /releases\/download\/v0\.2\.0-beta\.1\//, `${label}: retired beta.1 package link`);
}
requireText(markdown, '将 `BobCoach.dll` 和 `BobCoach.dll.sha256` 一起复制', 'Markdown 1.0.0 two-file install');
requireText(html, '将 <code>BobCoach.dll</code> 和 <code>BobCoach.dll.sha256</code> 一起复制', 'HTML 1.0.0 two-file install');
requireText(chineseReadme, '把 `BobCoach.dll` 和 `BobCoach.dll.sha256` 一起复制', 'Chinese README 1.0.0 two-file install');
requireText(englishReadme, 'Copy both `BobCoach.dll` and `BobCoach.dll.sha256`', 'English README 1.0.0 two-file install');
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
