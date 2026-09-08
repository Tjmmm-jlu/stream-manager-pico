# Stream Manager PICO 项目交接文档

> 用途：让另一台电脑上的 Codex、Unity 开发者或实验协作者能够直接理解项目背景、当前工程状态、已经完成的系统能力和下一步连续实验开发计划。
>
> 最后核对日期：2026-08-29
>
> 重要说明：本文档记录的是当前工程和最近一次真机验证得到的事实。旧的 `AGENTS.md`、`架构说明.txt`、`设计方法.txt` 可能保留早期 Quest、旧相机或旧提示方法的描述；如果与当前工程冲突，应以当前场景、脚本、`Packages/manifest.json` 和本文档为准。

## 1. 项目背景与研究主线

### 1.1 研究问题的来源

虚拟现实适合远程培训、教育、工业辅助、展馆引导和娱乐，但 VR 用户处于沉浸式三维环境，外部协作者通常使用 PC、平板或手机。双方的视角、设备、输入方式和可获得的信息不同，外部协作者难以直接表达“我希望你关注哪个物体、哪个区域或哪个操作步骤”。VR 用户也可能看错物体、没有看到目标，或者没有理解指导。

本项目聚焦 2D-PC 到 VR 的非对称协作：PC 端用户负责观察和指导，VR 端用户佩戴 PICO 头显完成任务。当前工程的重点不是单纯传输视频，而是研究如何让 PC 指导者更自然地表达协作意图，并让 VR 用户更可靠地确认和完成目标操作。

### 1.2 早期项目背景和演进

项目最初是一个 VR-PC 远程协作原型，核心目标是打通 PICO 真机、Unity 场景、WebRTC 视频流、浏览器查看和远程输入。PC 用户通过浏览器观看 VR 端画面，并通过点击或面板向 VR 用户提供目标提示；VR 端主要通过目标高亮接收提示。

早期阶段解决了以下基础问题：

```text
PICO 应用能在真机启动
Unity 场景能通过 Render Streaming 输出到浏览器
浏览器能接收视频和远程输入
PC 与 PICO 能通过信令服务器建立连接
PC 端的目标确认命令能传回 Unity
VR 端能对最终目标进行高亮
```

在开发过程中，曾经加入过控制面板、场景密度切换和若干提示脚本，也曾为了排查 PICO 闪退回退到没有面板和附加脚本的场景。当前工程保留了已经验证稳定的通信、语义、眼动和连续实验基础，不应把早期安装包与当前工程进行简单对比；排查问题时应以当前源码、当前场景和当前 APK 为准。

### 1.3 研究重点的转变

现有非对称协作研究更多关注 VR 用户如何接收和理解指导信息，常见方法是高亮、箭头、标注、引导线和空间框。相对较少讨论外部指导者如何自然、高效地表达指导意图，以及复杂场景中如何准确指定目标。

因此，项目的系统研究部分转向“外部指导者如何指定目标”。当前设计将 Unity 场景中的物体名称、类别、颜色、形状、材质、功能、内容物、状态和空间位置结构化注册。指导者输入自然语言后，系统提取物体属性，与场景语义数据库匹配，返回候选物体。存在多个外观相似或语义相同的候选时，再使用指导者的粗粒度眼动作为空间线索，对候选进行筛选或重排。

眼动在当前工作中不是像素级或物体级的精确选择器，而是有误差的低成本、隐式空间约束。PC 指导者通过候选框和候选截图确认目标；VR 端统一使用最终目标高亮，以减少提示呈现方式对实验结果的影响。

### 1.4 当前章节和后续章节的关系

当前章节主要回答：

> 指导者希望 VR 用户关注什么？

它使用自然语言、场景语义和粗粒度眼动完成目标指代、候选生成和目标确认。

后续共同注意力工作主要回答：

> VR 新手是否真正看到了、理解了并正在操作指导者指定的对象？

它计划采集 PC 指导者和 VR 新手的眼动，将双方注视映射到共享任务空间，计算共同注意置信度，并在注意力错位时自适应修复。

两项工作统一到同一条大主线：

> 利用多模态信息支持跨现实协作者之间的目标表达、意图对齐与共同注意。

当前项目是这条主线中的“目标指代与确认”部分；共同注意力是后续的“理解判断与错位修复”部分。

### 1.5 为什么转向连续化学实验

如果只做一次物体点选，鼠标点击可能已经足够，语言、语义和眼动融合的优势不容易体现。因此，场景需要从一次性目标选择扩展为具有前后依赖的连续任务：每一步都需要目标指代，目标分布在不同工作区域，VR 用户要寻找、抓取、放置或操作对象，下一步依赖前一步状态。

当前推荐使用“液体转移、混合与过滤”的模拟化学实验。重点是连续目标指代和跨现实协作，不是化学知识考核，也不要求第一版实现复杂流体物理。连续任务可以自然产生目标确认时间、错误动作、澄清次数、步骤完成时间和整体任务时间等客观指标。

## 2. 工程和版本

### 2.1 路径和仓库

Unity 工程：

```text
D:\unity project\stream manager pico
```

GitHub 仓库：

```text
https://github.com/Tjmmm-jlu/stream-manager-pico.git
```

已知提交：

```text
0ecba3b fix: gate stream camera until render texture is ready
8d6a195 first_v1
```

开始任何工作前执行：

```powershell
Set-Location "D:\unity project\stream manager pico"
git status
git log --oneline -5
```

修改场景前先复制一个带日期的场景备份。不要回滚其他开发者的修改，不要删除 `Library` 缓存来解决未知问题，除非已经确认需要重新导入。

### 2.2 Unity 和依赖

```text
Unity 2022.3.62f3c1
PICO Unity Integration SDK 3.4.0
Unity Render Streaming 3.1.0-exp.7
Unity Input System 1.14.0
XR Interaction Toolkit 3.0.3
```

`Packages/manifest.json` 当前把 PICO SDK 写成本机绝对路径：

```text
file:C:/Users/Administrator/Downloads/PICO Unity Integration SDK-3.4.0-20260226
```

另一台电脑必须安装同版本 SDK，并将 manifest 中 `com.unity.xr.picoxr` 改成该电脑可访问的路径。不能只复制 `Assets` 后直接认为项目可编译。

当前真机配置记录：

```text
平台：Android
架构：ARM64
图形 API：OpenGLES3
设备：PICO A8110
设备系统：PICO OS 5.13.7 / Android 10
包名：com.defaultcompany.streammanagerpico
版本号：0.1
Android bundle version code：1
```

### 2.3 主场景

```text
Assets/Scenes/ContinuousExperiment_AStage.unity
```

`ProjectSettings/EditorBuildSettings.asset` 中该场景是当前唯一启用的实验场景。

主要根对象：

```text
PICO XR Origin
Main Camera
Camera Offset
Left Controller
Right Controller
XR Interaction Manager
streamcamera
PreparationStation
MixingStation
FilteringStation
complexSceneObj
Experiment Distractor Layout
Distractor Spawn Area
ExperimentLayoutBounds
ExperimentGeneratedObjects
```

已知的大致位置：

```text
PICO XR Origin        (4.35, 1.00, 6.10)
PreparationStation    (3.407, 1.386, 6.987)
MixingStation         (4.249, 1.386, 5.836)
FilteringStation      (5.114, 1.386, 6.773)
```

这些位置只用于定位，最终要以 Unity Scene 视图、PICO 实际佩戴后的视线和浏览器视频画面为准。

## 3. 当前实现状态

### 3.1 已经完成或已验证

```text
PICO 真机运行基础
WebRTC 视频流和浏览器远程输入
信令服务器连接
Unity 场景语义数据库
场景物体的 SemanticObject 注册
PC 端自然语言属性解析接口
基于语义属性的候选匹配和加权排序
候选框和候选截图可视化
粗粒度 gaze 数据接入和候选重排
Low / Medium / High / Clear 干扰物生成接口
VR 端最终目标高亮
连续步骤控制器基础
JSONL 实验日志基础
```

### 3.2 尚未完成，不能提前声称完成

```text
完整的化学物理模拟
所有连续步骤的有效 target 引用
自动判断放置、倒液体、搅拌、加热和过滤完成
未完成当前步骤时阻止 next_step
三个工作台同时受统一密度控制
成熟的具体 STT SDK 适配实现
正式参与者实验和统计分析
可发布的 Unity Package 封装
```

### 3.3 当前最重要的已知阻塞

此前序列中曾错误加入 `test_tube_rack`，但当前实验并不需要试管架。该步骤已从主场景中删除，现阶段以 7 个已有真实物体构建连续任务。`BeforeRackRepair` 场景仅作为历史备份保留，不应恢复其中的试管架修复内容。

此前新增的 `ContinuousExperimentRackRepair` 编辑器工具属于错误方案，现已删除。后续不应执行或重新添加该工具。

## 4. Unity 端架构

### 4.1 当前数据流

```text
PICO Unity APK
    |
    | WebRTC 视频 + DataChannel
    v
PC 浏览器 viewer
    |
    | Transcript、语义匹配、候选可视化、gaze 排序
    v
PC 指导者确认 targetId
    |
    | guidance command
    v
Unity GuidanceManager
    |
    v
VR 场景目标高亮
```

眼动数据流：

```text
PC 眼动仪
    -> 本地眼动桥接 WebSocket
    -> 浏览器 mapGazeToVideoViewport()
    -> gaze DataChannel
    -> Unity GazeDataChannelReceiver
```

### 4.2 关键 Unity 脚本

`Assets/Guidance/GuidanceManager.cs`：

```text
当前主要模式为 none 和 highlight
使用 MaterialPropertyBlock 临时修改目标 Renderer 外观
清除目标时恢复原始属性块
不依赖箭头、路径或额外相机几何体
```

`Assets/Guidance/GuidanceDataChannelReceiver.cs`：

```text
接收 guidance JSON 命令
读取和刷新 SceneObjectRegistry
返回对象目录和候选信息
投影候选物体包围盒
接收候选预览列表
接收最终 targetId 并调用 GuidanceManager
切换 low / medium / high / clear
启动、前进、后退和重置序列
返回 Unity 状态
```

`Assets/Guidance/AStage/SemanticObject.cs` 和 `SceneObjectRegistry.cs`：

```text
SemanticObject 保存物体语义和交互能力
SceneObjectRegistry 建立 Stable ID 到对象的映射
Unity FindCandidates() 支持查询文本、类别和颜色的基础过滤
完整多属性解析和加权排序主要在浏览器端完成
```

`Assets/Guidance/PreparationSequenceController.cs`：

```text
保存步骤 ID、指令文本和目标 GameObject
提供 StartSequence、ShowStep、NextStep、PreviousStep、
CompleteSequence、ResetSequence
当前 NextStep 主要是切换步骤，不检查动作完成
```

`Assets/Guidance/AStage/ExperimentStateController.cs`：

```text
Idle
WaitingForInstruction
GeneratingCandidates
AwaitingConfirmation
TargetConfirmed
GuidanceShown
OperatorActing
Completed
Aborted
```

当前它以 trial 保存条件、目标 ID 和候选 ID，没有独立的每一步动作完成状态。

`Assets/Guidance/AStage/ExperimentInteractionTracker.cs`：

```text
扫描带 XRGrabInteractable 且父级有 SemanticObject 的对象
记录 target_grab_started、other_object_grab_started、object_released
抓取当前目标时把 trial 状态设为 OperatorActing
当前不判断放置位置是否正确
```

`Assets/Guidance/AStage/ExperimentLogger.cs`：

```text
日志目录：Application.persistentDataPath/ExperimentLogs
文件：session-<sessionId>.jsonl
字段：utcTime、unityTime、sessionId、eventType、trialId、
condition、state、targetId、candidateIds、message
```

继续开发时应在日志中增加或在 `message` 中结构化保存：

```text
stepId
语音文本
候选生成时间
确认时间
抓取时间
放置时间
错误计数
澄清次数
density
random seed
gaze 有效比例、延迟和目标距离
```

## 5. 语义数据库、自然语言和眼动匹配

### 5.1 场景语义数据库

当前场景有 18 个 `SemanticObject`。主要 Stable ID：

```text
complex_beaker_water
florence_flask
complex_china_dish
complex_test_tube_empty
complex_test_tube_liquid
complex_florence_flask_empty
complex_crucible_tongs
spirit_lamp
complex_crucible_with_cover
complex_glass_funnel
complex_graduated_cylinder_liquid
complex_spirit_lamp_water
complex_test_tube_water
dropper
china_dish
glass_funnel
beaker
graduated_cylinder
```

字段包括：

```text
stableId
displayName
objectType
category
function
color
transparency
shape
materials
contents
contentColor
stateTags
aliases
selectable
grabbable
```

多个对象可以有相同的 `displayName`，但 `stableId` 必须唯一且稳定。不要使用 Unity 自动生成的实例 ID 作为跨端协议中的目标 ID。

### 5.2 自然语言解析和匹配

浏览器端当前使用可解释的词典和规则解析，不是深度学习模型。输入可以是 STT 输出的文本，也可以是手动输入的文本。解析器将句子拆成若干属性约束，例如物体类别、颜色、形状、材质、内容物、状态、功能和空间关系。

匹配流程：

```text
Transcript 文本
    -> 规范化和别名展开
    -> 提取属性约束
    -> 遍历 object catalog
    -> 计算各属性匹配分数
    -> 归一化并加权排序
    -> 返回候选列表
    -> Unity 投影候选框并生成预览
    -> PC 指导者确认 targetId
```

当前建议的字段权重：

```text
objectType      100
contents         40
stateTags        40
contentColor     30
color            30
transparency     30
materials        25
shape             20
function          15
category          10
```

排序时不能只看最高分，还要保留候选的匹配解释，例如命中的词、未满足的属性、语义分数、gaze 分数和最终分数。这样便于实验记录和定位错误。

### 5.3 粗粒度 gaze 候选排序

当前默认参数：

```text
semanticWeight = 0.7
gazeWeight = 0.3
sigma = 0.12
regionRadius = 0.15
maxAgeMs = 1200
```

当前计算形式：

```text
gazeScore = exp(-(distance^2) / (2 * sigma^2))

combinedScore = 0.7 * normalizedSemanticScore
              + 0.3 * gazeScore
```

其中 `distance` 是 gaze 点与候选物体在视频视口或归一化空间中的距离。gaze 数据过期、坐标无效或无法映射到视频视口时，不应把无效 gaze 当成准确空间约束，应回退到语义排序并记录原因。

当前候选流程是：

```text
语义匹配
    -> preview_candidates
    -> Unity 使用 streamcamera 投影候选包围盒
    -> 返回 candidate_visuals
    -> 浏览器绘制候选框和候选截图
    -> 指导者确认 targetId
    -> Unity GuidanceManager 高亮目标
```

候选框和截图目前只给 PC 指导者看，VR 端只显示最终确认目标的高亮。gaze cursor 本身可以不在 PC 画面中持续显示；实验日志仍然应保存 gaze 坐标、有效性、候选距离和排序变化。

## 6. 密度生成器和连续场景搭建

### 6.1 当前密度生成器

文件：`Assets/Guidance/AStage/ExperimentDistractorLayoutGenerator.cs`

当前设置：

```text
lowCount = 4
mediumCount = 8
highCount = 10
randomSeed = 20260811
minimumSpacing = 0.04
targetClearance = 0.08
attemptsPerObject = 80
generateOnStart = false
```

干扰物池包括烧杯、锥形瓶、佛罗伦萨烧瓶、量筒、蒸发皿、漏斗、试管、坩埚、镊子和坩埚钳。生成器会清除旧对象，根据桌面 `Renderer Bounds` 刷新区域，避开序列目标，随机放置和旋转，检查越界与重叠，补 Collider 和 SemanticObject，最后刷新 Registry。

生成的干扰物默认 `selectable=true`、`grabbable=false`。它们可以作为 PC 候选和点击目标，但 VR 用户不应把它们当作必须移动的实验器具。

当前生成器只处理一个 `placementArea`。场景中的 `layoutRegion` 为空，`ExperimentLayoutBounds` 只有普通 `BoxCollider`，没有挂 `ExperimentLayoutRegion`。如果要求三个工作台同时由 Low / Medium / High 控制，需要扩展生成器支持多个区域，为每个区域设置独立边界、数量和 seed。

### 6.2 连续实验场景原则

场景要服务于连续任务，而不是单纯堆放更多模型。第一版必须满足：

```text
有固定初始状态和结束状态
步骤之间有明确依赖
每一步都需要目标指代
目标分布在至少两个空间区域
存在同类或外观相似干扰物
VR 用户有可观察动作
错误动作可以记录
相同 seed 可以复现相同布局
```

推荐三个区域：

```text
PreparationStation：初始器具领取
MixingStation：液体转移和混合
FilteringStation：过滤和结果处理
```

第一版不应一开始加入复杂流体物理、粒子、真实化学反应或新的 Camera/后处理。先保证目标指代、跨区域寻找、抓取、放置和错误记录完整。

### 6.3 场景搭建顺序

1. 复制 `Assets/Scenes/ContinuousExperiment_AStage.unity`，生成带日期的备份。确认原场景能打开、Unity Play Mode 正常、PICO 不闪退、浏览器能接收视频。

2. 先写清楚实验名称、初始状态、步骤顺序、每步语言意图、目标 Stable ID、目标区域、VR 动作、完成判定、错误动作、下一步解锁条件和结束状态。

3. 优先使用现有 7 个实验目标：`china_dish`、`graduated_cylinder`、`beaker`、`dropper`、`florence_flask`、`spirit_lamp`、`glass_funnel`。试管架不属于当前任务，不要添加。

4. 先确认 7 个目标均有唯一 Stable ID、`SemanticObject` 和 `Collider`，再配置目标区域与连续步骤。不要通过新增器具解决步骤缺失问题。

5. 所有需要 PC 射线选中的物体必须有 Collider。从 Collider 向上必须能找到 `SemanticObject`。需要 VR 拿起的物体必须有 Rigidbody、`XRGrabInteractable`、正确交互层和合理抓取点。

6. 先摆放目标，再摆放干扰物。目标要完全落在台面上，不能嵌入桌面、墙或其他物体；要能被 streamcamera 投影；VR 用户要能够到达；相邻物体之间要保留抓取空间。

7. 给目标标记区域，例如 `region_preparation`、`region_mixing`、`region_filtering`。空间关系尽量使用“位于 MixingStation 左侧”等可复现描述，不要只使用模糊的“左边”。

8. 配置 Low / Medium / High / Clear。第一版保持数量差异明显：Low 4 个、Medium 8 个、High 10 个、Clear 只保留静态目标。切换密度不能改变目标、步骤、语义和初始状态。切换后必须重新刷新目录和重新匹配候选。

9. 固定出生位置和相机。`PICO XR Origin` 要位于可到达区域；`Main Camera` 由 PICO 运行时控制；`streamcamera` 跟随 Main Camera；`TargetTexture` 由 Render Streaming 创建。不要在 `Awake` 中创建第二个 Camera，也不要把 Main Camera 当作普通物体长期锁死。

## 7. 连续化学实验流程

### 7.1 推荐任务

推荐任务名称为“液体转移、混合与过滤模拟实验”。步骤的科学意义不需要过度复杂，重点是让目标分散在不同空间区域，并要求 VR 用户执行连续动作。

当前序列控制器记录的 7 步为：

```text
1. beaker：找到装有液体的烧杯，并放到取液区域
2. graduated_cylinder：找到量筒，量取指定体积的液体
3. florence_flask：找到佛罗伦萨烧瓶，并放到混合区域
4. dropper：找到玻璃滴管，将少量液体转移到烧瓶中
5. spirit_lamp：找到酒精灯，放到烧瓶下方并完成加热
6. china_dish：找到蒸发皿，放到过滤区域作为接收容器
7. glass_funnel：最后找到玻璃漏斗，放到蒸发皿上方完成过滤
```

这 7 步是面向跨现实协作的连续模拟任务，不追求完整化学仿真。每一步都包含“目标指代、候选确认、VR 抓取/放置或操作、成功/失败记录”，后续再逐步补齐转移、加热和过滤的触发区判定。

### 7.2 建议的单步交互循环

```text
进入当前步骤
    -> PC 指导者说出目标描述
    -> STT 或文本接口生成 Transcript
    -> 浏览器解析属性并生成语义候选
    -> gaze 可用时对候选进行空间筛选或重排
    -> PC 查看候选框/截图并确认 targetId
    -> Unity 高亮最终目标
    -> VR 用户寻找、抓取、放置或操作
    -> Unity 判断动作完成或错误
    -> 记录事件和时间戳
    -> 完成后解锁下一步
```

指导者不能直接跳过未完成步骤。若研究需要允许跳过，应将其作为明确的实验事件记录为 `step_skipped`，不能静默改变状态。

### 7.3 第一版动作判定

先实现可重复、容易解释的判定：

```text
抓取目标是否正确
释放位置是否落在指定工作台区域
物体是否进入指定放置区
是否抓取了干扰物
是否在错误步骤操作
```

倒液体、搅拌、加热和过滤可以先用触发区、状态变量和计时器模拟，不需要立即实现真实物理。每种动作都要定义成功、失败、取消和超时事件。

## 8. 浏览器功能、服务和协议

### 8.1 浏览器目录

浏览器 viewer：

```text
D:\webrtcs\WebApp\client\public\viewer
```

关键文件：

```text
js/main.js
js/transcript.js
js/object-matcher.js
js/gaze-candidate-ranking.js
js/semantic-match-ui.js
js/candidate-visualizer.js
js/sequence-controls.js
js/object-catalog.js
```

### 8.2 浏览器已有功能

```text
接收 Unity 视频流
接收远程输入
接收或输入 Transcript
解析自然语言属性
加载 Unity 场景对象目录
根据语义属性生成候选
接收候选包围盒和截图
在 PC 端显示候选
接收 gaze 并重排候选
确认最终目标并发送 targetId
切换 Low / Medium / High / Clear
控制序列开始、上一步、下一步和重置
刷新对象目录
```

候选截图和可视化是指导者端的辅助信息，不能替代最终目标确认。VR 端仍然只显示最终高亮。

### 8.3 STT 接口约定

语音识别模块由其他工作提供时，浏览器只需要接收统一的 Transcript 数据，不应依赖某个具体语音识别 SDK。推荐最小格式：

```json
{
  "text": "请拿起左边装有水的烧杯",
  "isFinal": true,
  "timestamp": 1720000000000,
  "source": "stt"
}
```

接口适配层只负责把外部 SDK 的回调转换为这个结构。`isFinal=false` 的中间结果可以用于预览，但正式匹配和实验日志应默认使用 `isFinal=true` 的结果。若 STT 不可用，应保留文本输入作为开发和实验备用入口。

### 8.4 DataChannel 顺序

当前 DataChannel 标签和用途必须保持：

```text
input     远程输入
gaze      眼动数据
guidance  目标、候选、密度和序列命令
```

修改标签、创建顺序或消息格式前，必须同步检查 Unity 接收端和浏览器发送端。不能只修改其中一侧。

## 9. 信令、串流和 PICO 稳定性

### 9.1 信令地址

同一台 PC 本地测试时可使用：

```text
ws://127.0.0.1
```

PICO 真机不能把 `127.0.0.1` 当成 PC。公网服务示例：

```text
ws://49.140.24.146:80
```

局域网服务示例：

```text
ws://192.168.1.105:80
```

地址必须根据运行信令服务的电脑和端口填写。Node Render Streaming 默认监听 80 端口；若出现 `EADDRINUSE :::80`，说明端口已被占用，应检查现有服务或改用其他端口后同步修改地址。

### 9.2 眼动桥接地址

当前本地眼动桥接 WebSocket 地址：

```text
ws://127.0.0.1:8765/gaze
```

眼动桥接运行在 PC 上，浏览器连接它后，将坐标映射到视频视口，再通过 `gaze` DataChannel 发给 Unity。真机不需要直接连接 PC 眼动桥接服务。

### 9.3 streamcamera 闪退结论

曾经在 PICO 真机出现：

```text
Fatal signal 11 (SIGSEGV)
thread: UnityGfxDeviceW
cause: null pointer dereference
libunity.so
```

最终定位到 `streamcamera` Camera 在 Render Streaming 尚未创建 `RenderTexture` 时已经启用，Unity 图形线程访问无效资源而闪退。关闭 `streamcamera` Camera 后不再闪退；随后保留了启动门控逻辑。

相关文件：

```text
Assets/tagg.cs
```

门控行为：

```text
TargetTexture 为空 -> 禁用 streamcamera Camera
RenderTexture 创建 -> 自动启用 Camera
RenderTexture 释放 -> 再次禁用 Camera
```

不要删除该逻辑。未经真机验证，不要添加新的 Camera、RenderTexture 或后处理。Unity Editor 能启动不代表 PICO Android 图形线程一定稳定；每次涉及相机、RenderTexture、PICO SDK 或 Render Streaming 的修改都要重新打包并用 ADB 观察日志。

## 10. 推荐后续开发顺序

当前目标是尽快获得一个可以连续运行的任务原型，先发现流程和系统问题，再做 cursor 大小等参数实验。

1. 固定并备份当前稳定版本，记录 Git commit、Unity 版本、PICO SDK 版本、信令地址和测试设备。

2. 确认 7 个实验目标均有唯一 Stable ID、SemanticObject、Collider，并能出现在对象目录中；不添加试管架。

3. 完成三台实验台的目标摆放，确认 VR 出生点、桌面高度、相机视线和抓取可达性。

4. 把连续任务从“切换步骤”改成最小可用状态机：进入步骤、等待确认、目标高亮、执行动作、成功/失败、解锁下一步。

5. 先实现抓取、放置和错误抓取判定，并将每个事件写入 JSONL。不要同时实现复杂化学物理。

6. 扩展密度生成器，使 Low / Medium / High / Clear 可以对三个实验区域工作，或先明确实验只在一个区域内改变密度。每次生成记录 seed 和实际对象列表。

7. 用固定 Transcript 验证语言候选，再接入真实 STT Transcript。先确认接口格式、最终文本和日志，再处理识别 SDK 的具体问题。

8. 在连续任务中验证四种条件：语言、语言加鼠标点击、语言加场景语义、语言加场景语义加粗粒度眼动。先做小规模 pilot，检查条件切换、候选排序、错误恢复和日志完整性。

9. 检查 PC 候选框/截图是否能帮助指导者辨识相似物体，检查 VR 最终高亮是否稳定，检查断线、重连、重置和清除密度后的状态一致性。

10. 稳定系统和实验流程后，再讨论 Gaze Cursor 大小、场景密度和正式数据采集。Unity Package 的即插即用封装应放在系统接口稳定之后，避免在功能仍变化时反复封装。

## 11. 实验条件、问题和评价指标

### 11.1 当前系统要回答的问题

当前章节的核心问题是：

> 在跨现实连续任务中，场景语义和粗粒度眼动能否帮助 PC 指导者更自然、准确地指定 VR 用户需要关注和操作的目标，并降低目标确认成本？

### 11.2 推荐条件

```text
语言
语言 + 鼠标点击
语言 + 场景语义
语言 + 场景语义 + 粗粒度眼动
```

鼠标点击是重要 baseline，因为单次目标选择中点击可能已经足够。连续多步骤任务用来检验新增方法是否能减少反复点击、澄清和中断，而不是只比较一次选择速度。

### 11.3 主要客观指标

```text
目标确认时间
Top-1 目标识别准确率
Top-k 候选命中率
候选数量
候选缩减率
步骤完成时间
整体任务完成时间
错误抓取次数
错误放置次数
错误确认次数
澄清和重新描述次数
鼠标点击次数
语音轮次和描述长度
步骤中断与恢复时间
有效 gaze 比例、延迟和目标距离
```

NASA-TLX、SUS、主观信心和满意度可以作为补充，但不能替代上述客观指标。正式实验还要记录目标、场景密度、随机 seed、实验条件、参与者和 trial 顺序。

### 11.4 连续任务的基础日志事件

建议至少包含：

```text
session_started
density_changed
sequence_started
step_started
transcript_received
candidates_generated
gaze_received
target_confirmed
guidance_shown
correct_object_grabbed
wrong_object_grabbed
object_released
placement_succeeded
placement_failed
clarification_requested
step_completed
sequence_completed
trial_reset
```

所有事件都要带 `sessionId`、`trialId`、`stepId`、条件、时间戳和相关 Stable ID。这样才能把候选阶段的收益和连续操作阶段的收益区分开。

## 12. 验收标准和给下一台 Codex 的工作规则

### 12.1 阶段验收标准

在进入正式实验前，至少要满足：

```text
Unity Editor 能打开主场景且无新增编译错误
PICO APK 能启动并进入 ContinuousExperiment_AStage
streamcamera 启动、建立流和断开流均不闪退
PC 浏览器能看到视频并发送远程输入
对象目录包含所有目标和干扰物，Stable ID 无重复
自然语言能返回可解释的候选排序
gaze 无效时能回退到语义排序
PC 能看到候选框或截图并确认目标
VR 能稳定高亮确认目标
Low / Medium / High / Clear 切换后对象目录和候选可刷新
连续任务能从第一步进入最后一步
连续任务仅包含 7 个现有实验目标，不再包含试管架步骤
错误抓取、放置失败和步骤完成能写入 JSONL
重置后场景、序列和日志状态一致
```

### 12.2 工作规则

```text
先读当前源码和场景，再修改
修改前告诉用户准备修改什么
优先小步验证，不要一次加入多个高风险模块
不要把浏览器功能误认为 Unity 端已经完成
不要把序列切换误认为动作状态机已经完成
不要把 Unity Editor 正常运行误认为 PICO 真机正常
涉及 Camera、RenderTexture、PICO SDK 时必须真机验证
保留 scene 备份和 Git 提交
所有跨端消息修改必须同时检查发送端和接收端
实验数据必须记录可复现的 seed、条件和时间戳
```

当前最直接的下一步不是封装 Package，也不是马上做 Gaze Cursor 大小实验，而是修复第 7 步引用、完成最小连续动作状态机，并在 PICO-PC 实际链路上跑通一条从准备到过滤的完整任务。确认这条链路稳定后，再扩展密度、实验条件和正式数据采集。
