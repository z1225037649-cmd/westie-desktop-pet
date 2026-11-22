# 柯基三维动作帧接入

应用中的柯基现在播放实际生成模型的连续透明帧，取代旧 `corgi-hd.png` 的局部单图变形。源资产和可编辑骨骼/形态键工程数据保留在 `art/corgi-image-to-3d/rigged/corgi-rigged.glb`。

## 数据与呈现

- 源 GLB SHA-256：`e8857443eb63ce2de49b698de3978515712a239f23b3d008ed01ce60e5151a55`。
- 24 个骨骼、独立上下眼睑、切分上下唇及内口腔、四个面部键和一个伏身修正键。
- 七个片段：Idle 4s、Blink 1.2s、Pet 4s、Eat 5s、PlayBall 4s、Sleep 5s、Wake 4s。
- 固定相机渲染为 420×420 RGBA，共 333 帧，约12fps。桌面 306×260 与小窝 286×212 视口播放同一模型对象。
- `assets/corgi-animation.frames` 嵌入 EXE，资源名 `WestiePet.CorgiAnimation`。运行时没有 Python、Blender、网络或外部模型文件依赖。
- `CorgiFrames.cs` 读取带索引的 PNG、逐帧锚点和1位小狗命中遮罩，最多缓存24张已解码帧。`CorgiSprite.cs` 负责片段播放、睡醒接续、镜像与透明命中。
- 原始三张 PBR 图像仍在 GLB；帧预览用纹理、法线与深度缓冲生成，不是 Cycles 的最终灯光渲染。眼睑与伏身姿态仍可进一步美术细修。

## 状态与道具

程序的 idle/pet/feed/play/sleep 分别对应 Idle/Pet/Eat/PlayBall/Sleep。Sleep 完成后保持末帧；叫醒执行 Wake，再循环 Idle。寝态请求摸摸、喂食或玩球时，先播放剩余起身动作，再开始请求片段；窗口截止时间包含恢复时长。

饼干与球已在片段中按同一相机渲染，程序不再为柯基另外添加会脱离嘴或爪的道具。球在1.5s实际接触后才滚动。每帧仍保留源 GLB 嘴/爪锚点，供界面定位与验证使用。

命中遮罩来自深度缓冲中的宠物网格，排除球和食物。固定平面 z=0、中心Y=1.10、跨度2.65，鼠标坐标通过现有正交相机反投影后直接查遮罩，不遍历模型三角形或在指针事件中解码帧。

## 验证

`qa/CorgiAnimationChecks.cs` 独立解析资源里的全部 PNG、帧时间、锚点与位遮罩，并检查实际帧变化、睡姿保持、睡醒首尾连续、寝态动作的完整时长、重播、固定动作时间暂停、镜像和球输入排除。`qa/BreedChecks.cs` 与 `qa/ArtworkUiChecks.cs` 检查生产视口、轮廓、界面切换与偏好保留。

```powershell
./qa/run.ps1 -CorgiOnly
```

完整应用检查还覆盖西高地、设置、输入、启动项及整帧性能。更新应用后运行 build.ps1，同步根目录程序、便携目录、ZIP、校验和与发布说明，再运行 qa/VerifyPortable.ps1。

## 边界

这是固定相机的预渲染播放，朝向仍使用镜像，不支持自由转身、四足行走或自由散步。切回西高地后恢复其漫步设置。源坐姿模型尾巴上端局部融合到背部，上端跟随躯干，只有下部自由区域保留小幅摆动。

原始静态导入 `.blend` 保留。此前启动 Blender 被审批服务错误阻断，本轮通过人工审批已保存 `art/corgi-image-to-3d/rigged/corgi-rigged.blend`；重新打开后核对24骨骼、五个形态键、七个动作和四张打包贴图，并验证了实际骨骼及闭眼变形，结果见同目录 `native-checks.json`。应用接入和本地构建均不需要 Blender。
