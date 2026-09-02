# 省 Token 操作指南(2026-08-22 固化)

## 背景
- 用户反馈 token 花费过多(之前 reasoningEffort: max 是主要烧钱源)
- 当前默认: deepseek-v4-flash + medium(settings.yaml 热加载)
- 79 轮实证: 本任务推理需求远未到 max 级别

## 配置(settings.yaml)
- 默认: deepseek-v4-flash + medium —— 主力(执行+常规分析)
- 按需: 复杂根因/架构设计临时切 high(Web Models 页或 settings.yaml)
- 避免: max(成本8-12倍, 本任务无此需求)
- 备份: settings.yaml.bak-{effort}-{timestamp} 保留回退点

## 操作纪律(省 token 关键, 比模型选择影响更大)

### 1. 全量重建节制
- 参数扫描: 混合池 30 局筛方向 → 60 局确认 → 仅甜点候选才全量(279×12seed)
- 全量是主要时间+token 成本(~15分钟/次), 非必要不跑

### 2. A/B 精简
- 方向判断: 混合池 30 局(同 seed 42)
- 确认: 60 局(seed 42+2026)
- 避免: 多个中间值各跑全量(先小样本筛选)

### 3. 输出控制
- run_code 只打印结论性数据(均值/对比表), 不打印大段 JSON
- 大文件处理(回放解析/全量报告)写文件, 不 print 全文
- 批量脚本一次性跑完, 汇总输出

### 4. 会话管理
- 长任务(目标模式)用 goal 工具, 避免重复上下文
- 子代理处理独立子任务(减少主会话上下文膨胀)
- 后台 job 跑长命令, 不阻塞等待

## 验证记录
- 2026-08-22: high→medium 调整, 备份 bak-high-20260822
- 之前: bak-effort-20260822-105420(max 配置)
