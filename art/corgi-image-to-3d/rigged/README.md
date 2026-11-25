# 柯基骨骼、面部与互动动作初版

主文件：`corgi-rigged.glb`。这是实际三维蒙皮与 morph 动画资产，不是静态图片变形。原始 Meshy 下载保留在上级 `source/corgi-meshy.glb`。

## 已制作

- 24 个关节：根、骨盆、脊柱、胸、颈、头、下颌、左右耳、左右前腿/脚、左右后腿/脚、尾链。
- 全部顶点都有最多四个骨骼影响，权重归一化。原始 87,641 顶点的 UV 保留，三张原始 2048×2048 PBR 图像逐字节保留并嵌入文件。
- 面部键：`Blink.L`、`Blink.R`、`JawOpen`、`Smile`。另有 `LieDownCorrective` 和九个 `LieDownTransition.*`，共14个形态键；胸腹横截面沿圆弧转动，保留胸深与肩宽，过渡修正避免入睡和起身途中皮肤翻折。
- 闭眼使用独立的上、下弧形眼睑和闭眼线；两侧分别拟合。眼睑小贴图来自原材质邻近皮肤/毛色区域，作为第四张嵌入图像。
- 沿实际嘴部口缝切分了 250 个跨缝三角形，上下唇在静态位置重合，开合时真实分离；新增内口腔、舌和小牙，避免拉出封闭皮膜。
- `MouthAnchor` 和 `PawAnchor` 是实际节点；食物和球各有独立节点与动画轨道。

## 动作

| 动作 | 时长 | 行为 |
|---|---:|---|
| Idle | 4 秒 | 呼吸、轻微转头、尾部微动和眨眼，循环恢复 |
| Blink | 1.2 秒 | 独立完整闭眼，再睁开 |
| Pet | 4 秒 | 歪头、眯眼、耳朵回收和轻摆尾，恢复坐姿 |
| Eat | 5 秒 | 低头接近食物，连续下颌开合咬食，食物逐渐消失，恢复坐姿 |
| PlayBall | 4 秒 | 前爪伸出，在 1.5 秒接触地面的球，之后球才滚动，前爪回收 |
| Sleep | 5 秒 | 从坐姿前倾并降低头胸，保留圆润胸腹与前腿支撑，闭眼后保持浅伏休息 |
| Wake | 4 秒 | 从 Sleep 末态开始，睁眼、轻张嘴并恢复坐姿 |

Sleep 与 Wake 是连续的一对。Sleep 播放完应保持末帧，Wake 从该姿态起身；独立 GIF 循环时会重新展示整段动作。`previews/interaction-demo.gif` 按摸摸→咬食→拨球→睡觉→起身连续播放，完整睡醒过程可以连着看。

## 查看与继续编辑

将 `corgi-rigged.glb` 导入 Blender（文件→导入→glTF 2.0），可编辑骨骼权重、形态键和导入的动画。也可用支持 glTF skin/morph/animation 的三维查看器播放。默认静态姿态为原来的坐姿，动作分别存储在 GLB 的七个 animation 中。

当前胸前穿模修复以 `corgi-rigged.glb` 和应用内的333帧为准。`corgi-rigged.blend` 与 `native-checks.json` 保留此前版本，尚未包含本次睡姿修复；本次沙箱拒绝启动 Blender，后台保存审批未完成。若要继续编辑当前模型，请导入新版 GLB，或运行 `tools/blender/save_corgi_rig_blend.py` 同步原生工程，再运行 `tools/blender/verify_corgi_rig_blend.py` 重开核验。

```powershell
& 'C:\Users\user\AppData\Local\Programs\Blender\blender-4.5.16-windows-x64\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/save_corgi_rig_blend.py
```

## 验证与边界

`rig-checks.json` 由独立 GLB 解码与蒙皮实现验证：绑定矩阵、顶点权重、真实面部位移、七个动作的采样、网格边长变化、地面接触、睡醒连续性、食物/球接触以及原贴图和 UV 保留。

`sleep-chest-checks.json` 使用同一独立解码器，检查实际导出的110个入睡/起身帧里胸部与前腿的72,151个三角面，排除共享顶点和重合的UV缝，不允许新增表面交叉。可运行 `tools/blender/verify_corgi_sleep_chest.py` 重新核验。渲染报告逐片段记录源GLB的SHA-256，打包器拒绝混入旧模型帧。

`sleep-volume-checks.json` 检查全部110个睡醒帧的胸部截面宽深、皮肤面积与边长伸缩，防止无穿模但胸部被压成薄片的退化；当前最小胸截面深度保留96.6%，没有边长塌到原35%以下的胸区边。原始网格是坐姿，本轮采用克制的浅伏休息，未压扁胸部追求完全贴地。

`previews/preview-checks.json` 检查实际透明帧的完整轮廓与 GIF 时长，并用渲染深度归属核对闭眼时原虹膜及眼内高光被新增眼睑几何覆盖。帧来自交付 GLB 的实际蒙皮与 morph 数据，以纹理、法线和深度缓冲渲染；这不是 Blender Cycles/PBR 最终灯光渲染。

这是可编辑动作初版。原模型为生成的坐姿单网格，尾巴上端与背部局部融合，故上端随躯干变形，只给下部自由区域保留小幅摆动；目前没有四足行走或自由散步。面部接缝和伏身睡姿还可进一步美术细修。

已经接入桌宠源码：`assets/corgi-animation.frames` 嵌入全部333帧和逐帧嘴/爪锚点、宠物专用命中遮罩，播放器按需解码并最多缓存24帧。CorgiOnly 的资源、播放、透明命中、睡醒衔接、模型/小窝切换检查通过；本轮由协调后的唯一执行者完成统一应用检查、build.ps1 和便携包核验。实现说明见 `docs/corgi-generated-runtime.md`。
