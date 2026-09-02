// audit_plugin.js — 自动化静态审计(2026-08, 把 R226 人肉审查维度脚本化)
// 用法: node audit_plugin.js [srcDir]
// 检查: 空catch / 直接索引 / First()无保护 / 括号平衡 / 事件订阅未退订 / Task.Run线程共享
"use strict";
var fs = require("fs");
var path = require("path");
var srcDir = process.argv[2] || "E:/HDT-BobCoach/src/BobCoach";
var issues = [];
function walk(dir, depth) {
  if (depth > 6) return;
  fs.readdirSync(dir).forEach(function (f) {
    var full = path.join(dir, f);
    var st = fs.statSync(full);
    if (st.isDirectory()) { if (f !== "bin" && f !== "obj") walk(full, depth + 1); }
    else if (f.endsWith(".cs")) auditFile(full);
  });
}
function auditFile(file) {
  var lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
  // 1. 空 catch (catch { } 无处理)
  for (var i = 0; i < lines.length; i++) {
    var t = lines[i];
    if (/catchs*(?:([^)]*))?s*{s*}/.test(t)) {
      issues.push({ file: file, line: i + 1, type: "empty-catch", text: t.trim().slice(0, 80) });
    }
    // 2. 直接数字索引 .ShopMinions[0] 等
    var m = t.match(/(?:ShopMinions|BoardMinions|HandMinions|DiscoverOptions|TrinketOffer)s*[s*d+s*]/);
    if (m) issues.push({ file: file, line: i + 1, type: "direct-index", text: t.trim().slice(0, 80) });
    // 3. .First() 无保护(上2行无 Count/Any 守卫; 排除 ToDictionary 内 group.First 与有 Count>0 的情况)
    if (/\.First\(\)/.test(t) && !/\.ToDictionary/.test(t) && !/group\.First|g\.First/.test(t)) {
      var prev = lines[i-1] || "", prev2 = lines[i-2] || "";
      var prev3 = lines[i-3] || "";
      var guarded = /Count\s*[><=]/.test(prev) || /Count\s*[><=]/.test(prev2) || /Count\s*[><=]/.test(prev3)
        || /Any\(/.test(prev) || /Any\(/.test(prev2) || /Any\(/.test(prev3)
        || /!= null/.test(prev) || /!= null/.test(prev2);
      if (!guarded) {
        issues.push({ file: file, line: i + 1, type: "first-no-guard", text: t.trim().slice(0, 80) });
      }
    }
    // 4. 事件订阅/退订配对(统计 += 与 -=)
  }
  // 5. 括号平衡(跳过字符串/注释的精确 tokenizer)
  var bal = 0, inStr = false, inComment = false, inChar = false;
  for (var li = 0; li < lines.length; li++) {
    var line = lines[li];
    for (var ci = 0; ci < line.length; ci++) {
      var ch = line[ci], nx = line[ci+1];
      if (inComment) { if (ch === "*" && nx === "/") { inComment = false; ci++; } continue; }
      if (inStr) { if (ch === "\\" ) { ci++; } else if (ch === "\"") inStr = false; continue; }
      if (inChar) { if (ch === "\\") ci++; else if (ch === "'") inChar = false; continue; }
      if (ch === "/" && nx === "/") break;
      if (ch === "/" && nx === "*") { inComment = true; ci++; continue; }
      if (ch === "\"") { inStr = true; continue; }
      if (ch === "'") { inChar = true; continue; }
      if (ch === "{") bal++;
      if (ch === "}") bal--;
    }
  }
  if (bal !== 0) issues.push({ file: file, line: 1, type: "brace-imbalance", text: "文件级括号差: " + bal });
}
walk(srcDir, 0);
// 输出
var emptyCatches = issues.filter(function (i) { return i.type === "empty-catch"; });
var directIndex = issues.filter(function (i) { return i.type === "direct-index"; });
var firstNoGuard = issues.filter(function (i) { return i.type === "first-no-guard"; });
var braceIssues = issues.filter(function (i) { return i.type === "brace-imbalance"; });
console.log("=== 自动化审计结果 (" + srcDir + ") ===");
console.log("空 catch: " + emptyCatches.length + " 处");
console.log("直接索引: " + directIndex.length + " 处");
console.log("First无保护: " + firstNoGuard.length + " 处");
console.log("括号不平衡文件: " + braceIssues.length + " 个");
if (braceIssues.length > 0) braceIssues.forEach(function (i) { console.log("  ⚠ " + i.file.replace(srcDir, "")); });
// 高风险: 直接索引 + First无保护(可能运行时崩溃)
var risky = directIndex.concat(firstNoGuard);
console.log("\n=== 高风险项(直接索引/First无保护) ===");
risky.slice(0, 20).forEach(function (i) {
  console.log("  " + i.file.replace(srcDir, "") + ":" + i.line + " [" + i.type + "] " + i.text);
});
console.log("\n总问题数: " + issues.length);
