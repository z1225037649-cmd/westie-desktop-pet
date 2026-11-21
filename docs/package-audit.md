# 便携包只读审计

审计时间：2026-11-21 21:22（Asia/Shanghai）。这是并行开发过程中的文件快照，不能替代主对话最终构建后的复核。

## 使用

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\qa\VerifyPortable.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\qa\VerifyPortable.ps1 -Version 0.1.0
```

默认版本为 `0.1.0`，也支持其他 `主版本.次版本.修订版本`。可以用 PowerShell 7 的 `pwsh` 替换 `powershell.exe`。检查失败会抛出终止错误，以 `-File` 运行时返回非零退出码；成功时返回含 `Success`、哈希和逐项检查结果的对象。

脚本不编译、不解压、不更改发布包、不刷新目录时间，也不上传 GitHub。这里只新增验证脚本与本报告，没有运行 `build.ps1`。

## 核对范围

- 根目录 EXE、便携目录 EXE、ZIP 内 EXE 的 SHA-256 必须三者一致。
- ZIP 只能有两个根级文件：`小白-西高地桌面宠物.exe`、`README.txt`。子目录、额外文件、重复条目和大小写不同的文件名均不通过。
- 便携目录 README 与 ZIP README 必须逐字节一致；README 还必须等于 `docs/portable-readme.txt` 展开指定版本后的内容。
- `dist/SHA256SUMS.txt` 必须只有当前 ZIP 的一条有效记录，SHA-256 与实际 ZIP 一致。
- `dist/release-notes.md` 必须等于 `docs/release-notes.md` 展开指定版本后的内容，不允许残留模板占位符。两个文本模板比较只忽略末尾 CR/LF 的差异；ZIP 内外 README 仍严格比较字节。
- 便携目录修改时间不能早于 ZIP、便携 EXE、README 中的最新修改时间，允许文件系统 2 秒精度差异。目录后续被用户操作更新也允许。时间戳只能说明元数据符合刷新约定，不能证明某次构建来源。
- 检查开始和结束会比较必需文件的大小与修改时间，以发现一般的并发构建；如发生变化应等构建完成后重跑。
- 解压目录已有设置、日志或其他额外项目仅列为信息并保留；它们不得出现在 ZIP。脚本不会删除用户文件。

源码新旧单独报告，始终标记 `SourcePackagingStatus = UNVERIFIED`；发现已知应用/构建输入晚于根目录 EXE 时警告，但不将该项混入“现有包文件一致性”的结论。时间较早也不能证明源码已打包，最终必须由主流程构建并再次核对。

## 当前包结果

本次最初运行时所有项目通过，且在 PowerShell 7.6.9 与 Windows PowerShell 5.1 下均成功运行。随后主流程更新了 README 和发布说明模板；21:22 再次核对的**最新结果为失败两项**：便携 README 与当前模板不符、dist 发布说明与当前模板不符。脚本准确发现了尚未重新打包的说明内容，没有改写任何发布文件。

其余核对项目通过：

| 项目 | 当前值/结果 |
| --- | --- |
| 版本 | `0.1.0` |
| 三份 EXE SHA-256 | `37e244c668cf78de6fe8889696803930750ce6386df0dee34e088be8f51d4d98` |
| ZIP SHA-256 | `3af61834d95ba6a843fe2ba3f1abd5db71a7efc890c3bb44e332e066d2dafa54` |
| 解压及 ZIP README SHA-256 | `ea7a74aeb7ecf2a7fa08be273e582f78071973f491f71285adf7e7783105099c` |
| ZIP 文件清单 | 严格仅 EXE 与 README.txt |
| SHA256SUMS | 与 ZIP 一致 |
| 便携目录与 ZIP 修改时间 | 均为 `2026-11-21T09:28:11.3168500Z`（北京时间 17:28:11） |
| 已有个人文件 | `小白.settings.xml`、`runtime.log` 均保留，且不在 ZIP 中 |

当前 `WestieModel.cs`、`SculptedCoat.cs` 晚于根目录 EXE；说明模板也已更新。本报告**不表示主流程正在进行的模型与动作改造已经打包**。主对话应在最终 `build.ps1` 完成后重新运行验证，并以那次结果为交付依据。

## 验证脚本自身的检查

已通过 PowerShell 语法解析及 Windows PowerShell 5.1 / PowerShell 7 实际运行。使用 TEMP 下的独立副本进行异常验证，并在结束后清理；没有为这些检查改动真实发布文件。

临时一致性样本的模板由其已打包文本还原，避免并行修改中的真实模板影响异常测试。样本通过核对；运行前后所有样本文件的 SHA-256 和修改时间均不变，包含设置和日志。另一个一致性样本使用 `-Version 9.8.7`，通过核对。

以下八类样本改动均被拒绝：根目录 EXE 被改动、便携 README 被改动、ZIP 加入设置、ZIP 重复 README、错误校验和、发布说明保留占位符、发布说明版本错误、便携目录时间落后。没有新增永久测试产物。

主流程负责 `build.ps1`、发布说明以及 `.gitignore` 白名单；本次仅拥有 `qa/VerifyPortable.ps1` 和本文件的修改。
