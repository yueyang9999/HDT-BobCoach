# Skills 评估：本地 Codex Skills + GitHub 可引入清单

> 日期：2026-08-13
> 用途：为 HDT-BobCoach 工程工作（C#/.NET WPF 插件 + Node 工具链 +
> PowerShell 脚本 + 严格发布流程 + 中文文档 + 隐私契约）筛选可用的
> Agent Skills，提高工程完成度与规范性。
> 评估对象：
> 1. 本地 `G:\software\Codex\skills`（80+ 个，Codex CLI 常用）
> 2. GitHub 公开 skills 仓库（2026-08-13 检索）

## 一、本地 Codex skills 适用清单

### Tier 1 — 高价值，直接对应项目工程规范

| Skill | 对应项目规范 | 用法 |
|---|---|---|
| `test-driven-development` | AGENTS.md「Add or update tests for behavior changes」 | 模拟器 Node 测试、C# 单测、L1 工具链测试 |
| `verification-before-completion` | AGENTS.md Validation 节「evidence before assertions」 | 任何"完成/通过"声明前先跑验证命令并展示输出 |
| `systematic-debugging` | 复现→最小化→假设→修复→回归 | 引擎自噪声、harness 编译、模拟器状态问题 |
| `requesting-code-review` / `receiving-code-review` | 代码审查纪律 | PR/分支合并前审查流程 |
| `finishing-a-development-branch` | codex/ 分支收尾（merge/PR/清理选项） | 开发完成后的集成决策 |
| `changelog-release-notes` + `version-bump` | semver、发布清单、禁 latest-final 命名 | 版本与 CHANGELOG 维护 |
| `writing-plans` / `make-plan` / `executing-plans` | docs/superpowers/plans 的 YYYY-MM-DD 计划制度 | 计划文档撰写与执行 |
| `generate-implementation-design` / `how-it-works` / `design` | docs/design 设计记录制度 | 设计文档 |
| `gh-fix-ci` / `gh-address-comments` | .github/ CI 工作流 | Actions 失败诊断、PR 评论处理 |
| `using-git-worktrees` | 多分支并行（codex/ 前缀） | 工作树管理 |
| `learn-codebase` / `smart-explore` | 新会话快速上手 | 模拟器/插件核心代码库初始化 |
| `security-best-practices` | 隐私契约（数据本地化、无网络依赖） | 安全评审 |

### Tier 2 — 有用但非关键

| Skill | 用途 |
|---|---|
| `humanizer-zh` | 中文文档润色（docs 中文优先） |
| `knowledge-agent` / `mem-search` | 项目知识库积累与检索 |
| `brainstorming` / `design-loop` / `define-goal` | 方案设计 |
| `subagent-driven-development` / `dispatching-parallel-agents` | 并行子代理编排（与 DSH subagent 能力互补） |
| `writing-skills` | 为本项目编写自有 skill |
| `file-organizer` | 产物目录整理 |
| `gitnexus-*`（8 个：cli/exploring/guide/impact-analysis/pdg-query/pr-review/refactoring/taint-analysis） | 本 DSH 会话已注册可用；PR 审查/影响分析/重构安全 |

### 不适用（主要类别）

- Web UI 系：figma-*、playwright*、screenshot、ui-styling、ui-ux-pro-max、
  frontend-design、design-system、webapp-testing（项目是 WPF 覆盖层，非 web）
- 部署系：cloudflare/vercel/netlify/render-deploy（无云部署）
- 第三方服务：sentry、linear、notion-*、standup、weekly-digests
- 无关技术栈：aspnet-core、winui-app、jupyter-notebook、speech、
  transcribe、wowerpoint、pdf（弱相关）、chatgpt-apps、openai-docs、
  mingli-master*、hatch-pet、yeet、babysit、migrate-to-codex、do

## 二、GitHub 推荐引入

### 高优先

| 仓库 | Stars | 推荐子集 | 理由 |
|---|---|---|---|
| [wshobson/agents](https://github.com/wshobson/agents) | 38.7k | 91 插件中取：`shell-scripting`（防御性脚本）、`tdd-workflows`、`git-pr-workflows`、`comprehensive-review`、`code-refactoring`、`javascript-typescript`（nodejs-backend-patterns / javascript-testing-patterns）、`security-scanning`（SAST/威胁建模）、`dependency-management`、`cicd-automation`、`deployment-validation`、`ship-mate`、`unit-testing`、`code-documentation`、`documentation-generation`、`dotnet-contribution`（弱相关参考） | 原生支持 Codex CLI（`.codex-plugin` 格式），与本机 Codex 环境兼容；工程规范覆盖面最全 |
| [anthropics/skills](https://github.com/anthropics/skills) | 169k | `pdf`（读 Firestone XML/证据文档）、`skill-creator`（写项目自有 skill）、`doc-coauthoring` | Anthropic 官方，质量最高 |
| [travisvn/awesome-claude-skills](https://github.com/travisvn/awesome-claude-skills) | 14.6k | 目录检索用 | 按需查找更多 skill |

### 低优先/备选

- [RC-Techworks/claude-skill-powershell-admin](https://github.com/RC-Techworks/claude-skill-powershell-admin)（1★，PowerShell 管理脚本——项目大量 .ps1，可参考）
- [LeastBit/Claude_skills_zh-CN](https://github.com/LeastBit/Claude_skills_zh-CN)（anthropics/skills 中文学习版）

### 明确不建议引入

- ComposioHQ/awesome-claude-skills（72k★，但以 MCP Gateway 商业推广为主）
- 各种 SaaS 自动化 skills（Notion/Jira/Slack/CRM 等）——项目无这些依赖
- 前端/设计/营销类 skills

## 三、接入方式（务实）

1. **本地 Codex skills**：无需安装——本 DSH 会话可直接读取
   `G:\software\Codex\skills\<name>\SKILL.md` 并遵循其流程。
   推荐按任务按需加载 Tier 1 的 8-10 个。
2. **GitHub 引入**（建议）：
   ```powershell
   git clone --depth 1 https://github.com/wshobson/agents.git G:\software\Codex\skills-wshobson
   # 或只取需要的插件子目录
   ```
   用前先读 `plugins/<name>/skills/*/SKILL.md`，按项目实际裁剪
   （例如 shell-scripting 是 bash 向，取其防御模式思想应用到 .ps1）。
3. **项目自有 skill**（可选后续）：用 anthropics `skill-creator`
   把本项目高频操作固化为 1-2 个自有 skill：
   - `hdt-bobcoach-validation`（AGENTS.md 验证命令集 + 证据格式）
   - `simulation-experiment`（T40 模拟器/L1 双策略对比标准流程）

## 四、建议优先级（提升工程完成度）

1. **本周**：verification-before-completion + test-driven-development
   用于 L1 工具链与模拟器后续开发（当前最大缺口是测试覆盖与验证纪律）。
2. **开发中**：systematic-debugging（引擎自噪声种子化项目）、
   requesting/receiving-code-review（合并前审查）、writing-plans
   （T41 计划文档）。
3. **发布前**：changelog-release-notes + version-bump + ship-mate
   （发布流程规范）。
4. **持续**：learn-codebase 初始化每个新会话；knowledge-agent 沉淀
   Season14 规则事实。
