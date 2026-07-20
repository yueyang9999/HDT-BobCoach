# Bob Coach 隐私说明

> 状态：源码仓库已公开。GitHub Release 尚未获得授权，本说明不表示存在可公开安装的发布包。

## 概述

Bob Coach 在本机处理酒馆战棋对局状态，不要求账号，也不主动收集 BattleTag、密码、支付信息、设备标识符或广告标识符。它不自动上传游戏日志、回放、用户档案或诊断文件。

## 本地读取与写入

插件可能读取 HDT 内存中的可见对局状态、`Power.log`/`Power_old.log`、HDT `config.xml`、Hearthstone `log.config`，以及自身的数据目录。它可在 `%APPDATA%\bob-coach\` 写入本地配置、诊断、对局相关快照、回放和经验证候选数据；这些内容可能包含对局模式、时间、可见对手状态或本机路径，应按个人数据处理。

`log.config` 默认只检查不写入。只有用户在 HDT 内的“Bob 教练”按钮查看目标路径、完整拟议变更并明确确认后，插件才可写入；文件变化冲突、无权限或拒绝确认时失败关闭。卸载不会自动删除共享 `log.config`。

## 网络行为

插件的核心本地基线不依赖 Bob Coach 自有服务。已披露的只读 HTTPS 请求仅用于：

- Firestone/Zero to Heroes 的聚合饰品统计候选；
- HearthstoneJSON 的精确游戏 Build 卡牌事实校验。

请求限制为这两个来源、使用 HTTPS，并且不上传 Power.log、回放、账号、对手、用户档案或设备标识。请求失败不会阻断本地基线；未验证数据不能进入生产评分或 UI 排序。数据来源和分发限制见 `DATA_SOURCES.md`。

## 删除与反馈

关闭 HDT 后，可运行包内 `UNINSTALL.ps1` 删除插件 DLL。只有显式指定 `-RemoveUserData` 才会删除默认 `%APPDATA%\bob-coach\` 数据目录；备份 DLL、共享 `log.config` 和外部 `BOB_COACH_DATA_ROOT` 不会被自动删除。

提交 Issue 前，请删除账号信息、密钥、完整日志、完整回放和绝对个人路径。新的网络端点、上传字段、存储路径、保留规则或账号功能必须先更新本文件。
