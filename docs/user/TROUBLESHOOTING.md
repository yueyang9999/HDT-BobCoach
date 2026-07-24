# 故障排查

先确认使用的是本仓库 [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases) 提供的完整 ZIP，并核对同版本 `.zip.sha256`；源码、CI 产物和第三方附件都不是官方安装包。

## 安装器提示完整性、清单或哈希失败

- 完全退出 HDT。
- 确认解压目录是完整包根目录，不要替换、删改或混合其中的文件。
- 重新核对 Release 提供的 `BobCoach-<version>-win-x64.zip.sha256`，再解压到新目录。
- 如果 Windows 标记 ZIP 或 DLL 来自其他计算机，先在“属性”中解除锁定，然后重新解压。
- 不要修改脚本、删除哈希行、关闭安全软件或绕过验证。

## 找不到插件目录

插件目录固定为：

```text
%APPDATA%\HearthstoneDeckTracker\Plugins
```

如果 `%APPDATA%\HearthstoneDeckTracker` 不存在，先正常启动并退出一次 HDT，再重新运行安装器。该路径不随 HDT 程序安装位置变化；不要使用 HDT 程序目录下的 `Plugins`，安装器会拒绝它。

## HDT 未显示 BobCoach

确认安装器输出为 `PASS installed` 或 `PASS upgraded`，并确认 `BobCoach.dll` 与 `BobCoach.dll.sha256` 都位于 `%APPDATA%\HearthstoneDeckTracker\Plugins`。两者必须来自同一个安装包；缺少 SHA 文件、手工改名或哈希不匹配时，插件会拒绝启用。完全重启 HDT 后，在 `Options > Tracker > Plugins` 中检查 BobCoach 是否启用。仍失败时，不要复制完整 `Power.log`；记录插件版本、Windows/HDT 版本、错误时间和最小已脱敏错误文本。

## Power.log 相关提示不可用

安装器从不修改 `log.config`。在 HDT 的“Bob 教练”按钮中查看拟议变更并自行决定是否确认。若拒绝、文件不可写或配置不完整，插件应仍可加载，但依赖 Power.log 的功能不可用；变更后需要重启 Hearthstone。

## 升级后异常

不要向 HDT 程序目录或多个副本手工覆盖 DLL，也不要单独替换 DLL 或 SHA 文件。对 AppData 用户插件目录运行安装器，或按 [回退](ROLLBACK.md) 恢复备份。公开报告时仅提交最小复现和已脱敏内容，禁止上传完整日志、回放、账号信息、密钥或绝对个人路径。

## “时空扭曲”选牌时提示图标被遮挡

“时空扭曲”使用炉石的特殊选牌画面，其渲染层可能盖住 HDT 常规覆盖层中的提示图标。这只影响提示图标的可见性，不影响 BobCoach 已完成的推荐计算，也不影响普通酒馆和饰品界面的提示位置。当前版本将其记录为已知限制；为避免破坏其他分辨率下已经验证的界面位置，不通过继续提高画布内层级或改动普通商店坐标规避。若后续处理，需要针对该画面单独验证独立顶层覆盖窗口。
