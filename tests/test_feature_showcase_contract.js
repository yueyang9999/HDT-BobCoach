'use strict';

const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const errors = [];
const features = [
  ['购买建议', 'docs/user/images/features/feature-01-buy.jpg'],
  ['升本建议', 'docs/user/images/features/feature-02-upgrade.jpg'],
  ['技能提示', 'docs/user/images/features/feature-03-hero-power.jpg'],
  ['饰品推荐', 'docs/user/images/features/feature-04-trinket.jpg'],
  ['发现选择', 'docs/user/images/features/feature-05-discover.jpg'],
];

function read(relativePath) {
  const fullPath = path.join(root, relativePath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`missing ${relativePath}`);
    return '';
  }
  return fs.readFileSync(fullPath, 'utf8');
}

function readJpegDimensions(filePath) {
  const data = fs.readFileSync(filePath);
  if (data[0] !== 0xff || data[1] !== 0xd8) throw new Error('not a JPEG');
  let offset = 2;
  while (offset + 8 < data.length) {
    if (data[offset] !== 0xff) {
      offset += 1;
      continue;
    }
    const marker = data[offset + 1];
    const length = data.readUInt16BE(offset + 2);
    if (marker >= 0xc0 && marker <= 0xc3) {
      return { height: data.readUInt16BE(offset + 5), width: data.readUInt16BE(offset + 7) };
    }
    offset += 2 + length;
  }
  throw new Error('JPEG dimensions not found');
}

const markdown = read('docs/user/FEATURES.md');
const html = read('docs/user/FEATURES.html');

for (const [title, imagePath] of features) {
  if (!markdown.includes(title)) errors.push(`Markdown missing ${title}`);
  if (!html.includes(title)) errors.push(`HTML missing ${title}`);

  const localPath = imagePath.replace(/^docs\/user\//, '');
  if (!markdown.includes(`](${localPath})`)) errors.push(`Markdown missing ${localPath}`);
  if (!html.includes(`src="${localPath}"`)) errors.push(`HTML missing ${localPath}`);

  const fullPath = path.join(root, imagePath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`missing ${imagePath}`);
    continue;
  }

  try {
    const { width, height } = readJpegDimensions(fullPath);
    if (Math.max(width, height) > 1600) errors.push(`${imagePath} exceeds 1600px: ${width}x${height}`);
    if (fs.statSync(fullPath).size > 1024 * 1024) errors.push(`${imagePath} exceeds 1 MiB`);
  } catch (error) {
    errors.push(`${imagePath}: ${error.message}`);
  }
}

for (const source of [markdown, html]) {
  if (!source.includes('只提供建议')) errors.push('missing advice-only statement');
  if (!source.includes('不会自动操作游戏')) errors.push('missing no-auto-play statement');
}

if (/<(?:img|script|link|source)\b[^>]+(?:src|href)=["'](?:https?:)?\/\//i.test(html)) {
  errors.push('HTML uses a remote resource');
}
if (/<script\b/i.test(html)) errors.push('HTML contains script');

if (errors.length) {
  console.error('FAIL feature showcase contract');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`PASS feature showcase features=${features.length} images=privacy-cleaned-local-only`);
