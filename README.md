# 小白 · 西高地桌面宠物

一只在 Windows 桌面陪伴你的西高地小狗。使用 C# 和 WPF 构建立体模型、透明桌面窗口与互动动画。

模型由程序生成，不需要另外下载 `.glb`、`.fbx` 或运行时模型服务。

![小白玩球](docs/images/play.gif)

## 下载使用

前往 [Releases](https://github.com/z1225037649-cmd/westie-desktop-pet/releases) 下载 Windows 便携版，解压后双击 **小白-西高地桌面宠物.exe**。

- 适用环境：Windows 10 / 11，启用了 WPF 的 .NET Framework 4.x。
- 无需安装，也不需要管理员权限；请放在可以写入文件的普通文件夹中。
- 首次运行打开“小白的小窝”；关闭小窝后，桌面宠物仍会继续运行。
- 托盘图标可以打开小窝、找回小狗或退出。

## 可以做什么

- 拖动小狗调整位置，滚轮调整大小。
- 触碰小狗后，通过操作条摸摸、喂食、玩球或打开小窝。
- 在小窝里让它睡觉、叫醒，或切换自由散步。
- 摸摸时，小狗会眯眼、左右蹭头、放松耳朵并摇尾巴。
- 喂食时，立体小饼干靠近嘴边，小狗低头咬食、咀嚼，饼干逐口变小并掉落碎屑。
- 玩球时，前爪轻拨立体网球，球会转动、滚动并逐渐停下。
- 睡觉时，小狗伏低身体、收起四肢、闭眼闭嘴，并保留轻微的呼吸起伏。
- 支持置顶、多显示器位置限制和屏幕外位置恢复。

![小白睡觉](docs/images/sleep.png)

快捷键在宠物窗口获得焦点时生效：

| 操作 | 快捷键 |
| --- | --- |
| 摸摸 | 空格 |
| 喂食 | F |
| 玩球 | B |
| 睡觉 / 叫醒 | S |
| 切换自由散步 | R |
| 收起操作条 / 取消拖动 | Esc |

右键小狗可打开完整菜单。大小、位置和散步设置保存在程序旁的 `小白.settings.xml`，该文件不会提交到仓库。

## 从源码构建

需要 Windows 上的 .NET Framework 4.x 编译器及 WPF 程序集。构建脚本直接调用系统 `csc.exe`，无需 NuGet、Node.js 或模型下载。

在仓库根目录打开 PowerShell，运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物位于根目录：`小白-西高地桌面宠物.exe`。若程序正在运行，请先从托盘菜单退出，再重新构建。

## 检查与渲染

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\run.ps1
```

检查内容包括屏幕边界、转头平滑、散步暂停、玩球时身体高度、摸摸蹭头眯眼、喂食咀嚼与饼干消耗，以及六种状态的透明画面渲染与互动状态切换。摸摸、喂食还会输出去除文字和符号的连续动作帧。

可选的多角度模型渲染：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\run.ps1 -RenderDetails
```

`qa/compose_previews.py` 可将运行自检生成的玩球帧合成为 GIF；这一步需要 Python 和 Pillow，应用本身不依赖 Python：

```powershell
python .\qa\compose_previews.py --frames-dir .\qa\qa
```

预览输出到 `qa/play-preview.gif`；若已运行多角度模型渲染，还会生成 `qa/sleep-preview.png`。

## 源码结构

| 文件 | 内容 |
| --- | --- |
| `Program.cs` | 程序入口、单实例与本地设置 |
| `PetWindow.cs` | 透明窗口、小窝、操作条、托盘和互动状态 |
| `WestieModel.cs` | 五官、姿态动画与立体网球 |
| `SculptedCoat.cs` | 连续身体表面、毛发与蒙皮变形 |
| `assets/` | 小窝图标与图标源文件 |
| `qa/` | 动作检查、渲染工具和交互验证程序源码 |

当前模型使用 WPF 自带的 3D 渲染。外形和动作仍在迭代，欢迎通过 Issues 提交建议与复现步骤。
