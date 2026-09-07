const fs = require('fs');
const p = 'E:/HDT-BobCoach/CHANGELOG.md';
let t = fs.readFileSync(p, 'utf8');
if (t.charCodeAt(0) === 0xFEFF) t = t.slice(1);
const anchor = '## 1.2.18 - 2026-09-07';
if (!t.includes(anchor)) { console.log('anchor missing'); process.exit(1); }
const entry = '## 1.2.19 - 2026-09-07\n\n- feat(shadow 采集族, 记录型不改可见推荐): recommend 事件新增 shadowC1(升级三条件)/shadowC6(5→6 本门: turnOk T≥10/boardOk≥8.0/gatePass)/shadowC7(并列: 顶二 gap<0.10 → nearTie/tieGap/topType)——300 场景门禁的 Shadow 语料采集(证据 V8 rho−0.63/V12-13 分值压缩)\n- feat(纪律/工具): AGENTS.md Task Execution Discipline; 离线引擎重评=版本漂移回归(40,357 状态同类型 100%)\n\n';
t = t.replace(anchor, entry + anchor);
fs.writeFileSync(p, '\ufeff' + t, 'utf8');
console.log('inserted');
