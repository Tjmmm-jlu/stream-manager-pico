# 当前控制代码说明

核对日期：2026-09-10。本文按当前 PICO 连续实验的实际代码整理；旧 AGENTS.md 中的 Quest、旧主场景和部分旧接口描述不代表当前运行配置。

当前场景：`Assets/Scenes/ContinuousExperiment_CopperSulfate_LargeTables.unity`。

## 1. 整体调用关系

指导者浏览器收到 Unity 视频和器材语义目录。描述输入经网页属性匹配产生候选；可用的指导者眼动参与候选排序。指导者确认后，`GuidanceDataChannelReceiver` 接收目标 ID，`GuidanceManager` 在 VR 中显示最终目标。

当前 LargeTables 场景中，VR 用户用左右控制器射线瞄准，按住 Grip 抓取、松开释放。各化学步骤检测器判断器材状态和操作条件，调用 `PreparationSequenceController.CompleteAction`。流程控制器确认动作与当前步骤一致后推进；状态、器材目录和日志随之更新。

目标提示与动作完成是两件事：确认了正确器材，不代表量水、过滤或加热已经完成。

## 2. 连续任务与状态

### PreparationSequenceController.cs

位置：`Assets/Guidance/PreparationSequenceController.cs`。

保存步骤 ID、说明、目标对象以及完成动作。核心入口为 `StartSequence`、`CompleteAction`、`NextStep`、`ShowStep`、`ResetSequence`。

当前七个动作依次是：

| 顺序 | completionAction | 完成条件由谁判断 |
|---|---|---|
| 1 | transfer_copper_sulfate | CopperSulfateTransferDetector |
| 2 | measure_water | CopperSulfateMixingDetector |
| 3 | dissolve_copper_sulfate | CopperSulfateMixingDetector |
| 4 | filter_copper_sulfate | CopperSulfateFiltrationDetector |
| 5 | transfer_filtrate | CopperSulfateFiltrationDetector |
| 6 | assemble_heating | CopperSulfateHeatingDetector |
| 7 | concentrate_solution | CopperSulfateHeatingDetector，包含灭火后的冷却结晶 |

`CompleteAction` 只有收到当前步骤要求的动作才接受，并防止重复完成。未完成前面的动作不能直接跳到后面的步骤。`Resetting` 事件让各检测器恢复器材、液体、晶体和安装状态。默认不因显示步骤就自动揭示目标，指导者仍需确认。

### ExperimentStateController.cs

位置：`Assets/Guidance/AStage/ExperimentStateController.cs`。

管理一轮指导过程的 trialId、condition、候选 ID 和目标 ID，以及 Idle、WaitingForInstruction、GeneratingCandidates、AwaitingConfirmation、TargetConfirmed、GuidanceShown、OperatorActing、Completed、Aborted 等状态。它记录的是指导状态，不能替代化学动作检测。

## 3. 手部、抓取与归位

| 脚本 | 当前职责 |
|---|---|
| PicoStablePinchInputReader.cs | 从 XR Hand 的 ThumbTip 和 IndexTip 读取距离；默认 2.5 cm 内持续 0.05 秒认定捏合，3.5 cm 外持续 0.06 秒认定松开。两阈值之间保持稳定，避免抖动。丢失跟踪时禁止新的抓取；已持有物体不会仅因一帧无效关节就被当成主动松手。 |
| PicoHandPinchGrabBootstrap.cs | 配置左右 XRDirectInteractor、捏合点跟踪和输入引用，关闭旧手柄交互器，并补齐归位组件。大桌场景已经序列化了相应手部组件；此脚本的自动场景名判断仍保留早期场景名称。 |
| ExperimentGrabReturn.cs | 记住物体的原父对象、位置、旋转和缩放。普通释放后在 LateUpdate 归位，关闭抛掷。程序取消抓取不触发普通松手归位，避免干扰支架安装和实验重置。 |
| ExperimentInteractionTracker.cs | 订阅语义器材的抓取开始与结束，记录拿到目标或其他器材；检查旧放置区域步骤。程序取消释放不作为用户确认的放置。 |
| ExperimentPlacementZone.cs | 表示旧任务的目标放置区域，提供区域 ID、允许物体和位置检测。当前化学操作的安装和流体步骤主要使用各自检测器。 |
| PicoExperimentStartPose.cs | 等待有效头显跟踪后，按 Stationary Start Pose 调整 XR Origin；支持网页请求重新定位。操作中有物体被持有时拒绝定位。 |

上述文件均在 `Assets/Guidance/AStage/`。

### 当前大桌场景的双手柄模式

当前大桌场景使用 `Left Controller` 和 `Right Controller` 的控制器射线交互。`PicoControllerGrabBootstrap.cs` 在场景加载时关闭 `HandLeft`、`HandRight`、手部捏合交互器和 `XRDirectInteractor`，为每个控制器创建一条 `XRRayInteractor`，因此物体不能通过手部模型或近距离碰撞直接选中。`PicoControllerRayTracking.cs` 会在 PICO 延迟生成手部对象时继续清理它们。

PICO SDK 的 `Assets/Resources/PXR_ProjectSetting.asset` 保持 `handTracking: 0`；`handTrackingSupportType: 0` 对应 SDK 的 ControllersAndHands 枚举值，配合关闭 handTracking 使用。这是全局构建设置。官方 HandLeft/HandRight 预制体还必须在场景中停用，仅关闭 handTracking 不代表场景手部网格已隐藏。本次射线改动未再次修改这些全局设置。

射线输入使用 XRI 3 的显式 Input Reader：位置和旋转使用 `<XRController>{LeftHand/RightHand}/devicePosition`、`deviceRotation`；握把按键 `gripPressed` 是按住抓取、松开释放；扳机 `triggerPressed` 只用于 activate，不会单独抓取；摇杆 `primary2DAxis` 的上下用于沿射线调整持物距离。射线最大长度为 5 米，射线命中后按住 Grip 才会选择。释放后器材是否归位仍由 `ExperimentGrabReturn` 决定，化学检测器继续通过 `XRGrabInteractable` 判断持有状态，因此不需要为每个实验步骤分别改写手柄逻辑。

双手实验操作方式是：用控制器射线瞄准器材，按住对应握把键抓取，松开握把键释放；需要双手同时持有时使用两个手柄分别抓住两个器材。当前代码不再读取拇指和食指距离，也不启用直接触碰。Unity 菜单 `PICO > Build LargeTables Controller-Ray APK` 会构建当前大桌射线场景；旧的 `Build Preparation APK` 仍用于旧准备场景。

抓取会将器材拉到控制器前方约 18 cm 的持物点，保留器材自身抓握点设置；转动控制器用于倾倒和搅拌。摇杆上下调距、左右调节持物朝向。射线空闲时左蓝右橙，命中可交互器材时变绿，墙壁等实体阻挡射线。左右手标识显式配置，双手化学检测继续生效。失去追踪时自动释放并隐藏对应射线。按下 Grip 的边沿才会开始选择，安装后需要先松开 Grip 再重新抓取，避免程序取消选择后立即重抓。

2026-09-11 已通过 Unity 编译和模拟 PICO Input System 输入检查，包括远距离命中、瞄准不自动抓取、左右独立按键、双手持物、摇杆调距、旋转、释放归位、实体遮挡、失去追踪，以及实际混合烧杯的射线抓取归位。射线渲染截图和报告位于 `Temp/ControllerRay/`。这些是编辑器验证，未验证头显上的完整连续实验。自动验证入口是 `Tools > Stream Manager > Controller Rays > Verify Input In Play Mode`。

本次专项备份：`MigrationBackups/BeforeControllerRay_20260911_141647_768`。其中 `Before/` 保存修改前文件，`After/` 保存修改后文件。`rollback.ps1` 默认只检查；关闭 Unity 后带 `-Restore` 才还原，检测到后续修改会拒绝覆盖。场景安装器还单独保存了磁盘场景及可能存在的未保存场景副本。

2026-09-11 射线可见性修正：PICO 真机上发现右控制器已经有有效位置和旋转（trackingState=3），但 isTracked 动作仍可能返回 0。旧版因此关闭射线。现在接受有效位置和旋转状态，并在动作尚未解析时使用原生 Unity XR 的有效追踪姿态作为补充；真正丢失追踪仍释放器材并隐藏射线。根据真机反馈，当前线宽从 7 mm 调细为 3 mm，保持完全不透明，并关闭动态遮挡剔除。启动前 35 秒每 5 秒输出一次 `[PICO Ray Visibility v2]` 诊断。

新版 `Builds/stream-manager-pico-large-tables-ray-20260911-151142.apk` 已构建并覆盖安装到已连接的 PICO（保留应用数据）。真机日志确认右控制器运动时 ray、visual、line 均启用，并生成两个线段端点，用户随后确认在头显内已看到射线。当时未检测到左控制器，不能据此确认左手真机表现。修正前的源码、场景和 APK 保存在 `MigrationBackups/BeforeControllerRayVisibility_20260911_150310_611`。

## 4. 晶体、液体、过滤和加热

### CopperSulfateTransferDetector.cs

检测镊子是否被持有、尖端是否进入晶体拾取范围并停留，再把晶体显示挂到镊子上。到达烧杯开口范围并满足停留条件后完成投放。持有中松手会取消未完成的搬运。这里使用位置与停留辅助判定，不模拟真实镊子两片夹紧晶体的接触力学。

### LiquidContainer.cs

保存容量、初始水量、当前水量、容器底部与开口参数，并更新液面网格及颜色。提供 `Add`、`Remove`、`SetVolume`、`ResetLiquid`、`LowestLip`、`TryReceive`、`ContainsLiquidPoint` 等接口。采用几何和体积模拟，不是完整流体求解器。

### LiquidPourInteractor.cs

检查双手分别持有不同容器、倾斜与液面条件，计算流出的水量，检测容器接收或洒出，并绘制水流。`TickGuidedIntoOpening` 用于过滤：检查烧杯、玻璃棒与漏斗入口的相对位置，允许玻璃棒任一端作为引流端，并检查路径阻挡。

### CopperSulfateMixingDetector.cs

测量目标为 100 mL，容差为正负 10 mL；量筒直立、没有持续倒水并稳定后完成量水。之后要求把水转移到烧杯，再用另一只手持玻璃棒搅拌。

搅拌按棒端相对于烧杯的有效横向运动累计，而不是按固定圈数计数。默认累计约 2 秒有效运动；静止、微小跟踪抖动、离开液体或不符合双手条件不会正常累计。允许使用玻璃棒两端，完成后更新溶液、晶体显示和语义状态。

### CopperSulfateFiltrationDetector.cs

管理原有支架上的漏斗和接水烧杯安装点、导流目标、滤渣显示和滤液体积。处理安装后的接水烧杯重新取出。引流与过滤完成后进入滤液转移阶段，向蒸发皿接收液体。代码中的最低收集量按本阶段初始液量的 90% 计算；具体运行状态随当前步骤变化。

### CopperSulfateHeatingDetector.cs

管理蒸发皿、酒精灯、灯帽、长柄点火器与支架安装点。开盖并接近灯芯后点火；记录蒸发进度，达到浓缩目标后等待盖帽灭火。灭火后进入 5 秒冷却倒计时，显示析出的晶体并结束实验。默认加热参数为约 20 秒、目标剩余比例 0.5；Inspector 中序列化参数优先。

这些检测器都订阅流程重置事件，因此更改布局不应重新创建或重新配置原有任务器材。

## 5. 网页指导与语义对象

| 脚本 | 职责 |
|---|---|
| SemanticObject.cs | 保存稳定 ID、显示名称、类型、类别、用途、颜色、透明度、形状、材质、内容物、状态和别名，以及是否可选、可抓。 |
| SceneObjectRegistry.cs | 收集场景语义对象，检查 ID，提供查找、目录和候选查询。两档场景中只注册激活对象，避免隐藏的高档物体泄漏到低档候选中。 |
| GuidanceDataChannelReceiver.cs | 处理 guidance 通道中的试次、步骤、候选预览、目标确认、布局、重新定位和阶段重试命令，回传目录、屏幕投影框和过程状态。 |
| GuidanceManager.cs | 执行最终目标提示与清除；当前主要使用物体高亮。 |
| PlaterController.cs / PlayerController | 把远程鼠标点击映射到串流相机射线，再交给目标提示逻辑。文件名保留了早期拼写。 |
| GazeDataChannelReceiver.cs | 接收网页映射后的 gaze 坐标，转换为串流相机射线并提供调试显示。不能把调试射线命中等同于网页候选融合算法。 |
| NewBehaviourScript.cs / StreamManager | 连接视频发送与远程输入组件，处理视频尺寸与输入区域。 |
| tagg.cs / StreamCameraFollow | 同步串流相机与 VR 相机姿态。 |

网页代码位于 `D:/webrtcs/WebApp/client/public/viewer/js/`：

| 文件 | 职责 |
|---|---|
| main.js | 连接串流和 guidance/gaze 通道，绑定网页控制，接收实验状态。 |
| transcript.js | 规范化外部输入的描述文本。 |
| object-catalog.js | 接收、规范化 Unity 器材目录。 |
| object-matcher.js | 基于类型、颜色、内容物、材质等属性和别名打分匹配。它是已有规则与权重实现，不应表述为已经验证的通用语言理解模型。 |
| gaze-candidate-ranking.js | 用眼动到候选屏幕框的距离作软约束；默认语义权重 0.7、眼动权重 0.3。无效、过期、没有可用投影框或候选不足时回退。 |
| semantic-match-ui.js | 管理描述提交、候选列表和确认。 |
| candidate-visualizer.js | 处理屏幕框、候选截图与预览更新。 |
| sequence-controls.js | 保留的步骤控制入口；当前页面实际加载和按钮绑定应以 index.html 与 main.js 为准，不要重复接入同一按钮。 |

这些模块已经实现了候选生成与融合接口；效果是否更好仍需后续实验验证。

## 6. 两档场景控制（本次新增）

`ExperimentDistractorLayoutGenerator.cs` 保留旧场景的随机 Low/Medium/High/Clear 行为。大桌场景配置 `ExperimentPairedLayoutController` 后，入口改为操作同一组固定对象，不再每次随机生成。

| 条件 | 任务物体 | 干扰物 | 合计 |
|---|---:|---:|---:|
| Low | 13 | 6，每桌 2 件 | 19 |
| High | 13 | 18，每桌 6 件 | 31 |

配对布局 A 使用固定种子 20260811。低档的 6 件干扰物与高档共用同一实例、同一稳定 ID 和同一位置。额外器材为真正可选、可抓、松手归位的对象，但不会触发任务步骤。

低档配置：准备桌增加量筒、试管；混合桌增加烧杯、锥形瓶；过滤桌增加烧杯、带盖坩埚。高档在每张桌上各增加锥形瓶、圆底烧瓶、试管和坩埚钳。新增器材中心与座位的水平距离限制为 1.35 米以内，优先选择近处可用位置；原有任务器材位置不变。较远的干扰物不保证坐着伸手即可触及，当前连续任务不要求操作它们。

这是一组整体场景复杂度条件：增加器材数量及同类干扰，不把各因素的独立贡献作为已经验证的结论。19/31 是可选择语义对象数，不含墙壁、家具、手部模型或液体子网格。两档是否产生足够且合适的实际难度差异，仍需后续试运行确认。

流程或试次进行中拒绝切换；持有器材时也拒绝切换。重置连续任务会恢复本档器材的位置，并保留当前 Low/High 选择。Medium、Clear 或尚未构建的 seed 会返回明确失败，不会伪报成功。

网页根据 Unity 返回的 `supportedLayoutDensities` 只显示本场景支持的两档，并显示对象数量与锁定状态；旧场景继续返回原来的四个选项。

Unity 菜单：`Tools > Stream Manager > Two Level Layout > Low (19 Objects)` 或 `High (31 Objects)`。菜单切换在编辑状态下保存场景；运行状态下仍遵守锁定条件。实际器材位置、数量与视图检查见 `Temp/PairedLayout/verification.txt` 和配对截图。

## 7. 编辑器脚本的角色

这些脚本负责构建和核验场景，不是每帧运行的化学控制器：

| 脚本组 | 作用 |
|---|---|
| CopperSulfateExperimentSceneBuilder、CopperSulfateTransferInstaller | 创建晶体转移场景及其对象、检测点和步骤。 |
| CopperSulfateLiquidInstaller | 安装液体、量筒、烧杯、玻璃棒及量水溶解步骤。 |
| CopperSulfateFiltrationInstaller、CopperSulfateStandFiltrationInstaller | 安装过滤逻辑与原支架上的装配结构。 |
| CopperSulfateHeatingInstaller | 安装点火器、灯帽、火焰、浓缩和结晶步骤。 |
| CopperSulfateApparatusFixInstaller | 修正接水烧杯与蒸发皿等器材配置。 |
| CopperSulfateErgonomicsInstaller、CopperSulfateStationarySceneBuilder | 调整工具大小、坐姿可达范围与静止操作场景。 |
| CopperSulfateLargeTableSceneBuilder | 恢复三张大桌并布置原有 13 个器材。其旧检查假定没有干扰物，不应直接把这一旧假定用于两档布局。 |
| CopperSulfateRoomInstaller | 补齐实验室墙壁和天花板。 |
| CopperSulfatePairedLayoutInstaller | 本次新增，构建配对布局、配置交互与目录、验证并输出截图。 |
| ExperimentDistractorLayoutInstaller、ExperimentVrLayoutSelector、ExperimentLayoutRegion | 早期随机密度布局、VR 面板与区域描述；对旧场景保留兼容。 |
| PicoHandPinchGrabInstaller、PicoOfficialHandPrefabInstaller、PicoExperimentInteractionSetup | 安装手部模型、输入、交互器和相机配置。 |
| AStageFoundationInstaller、AStageSemanticCatalogInstaller、GuidanceSceneInstaller | 安装基础语义、状态、日志和指导通道。 |
| PreparationExperimentSceneBuilder、OriginalLaboratoryWorkstationBuilder、RestoreOriginalLaboratoryShell | 早期任务和实验室结构构建工具。 |
| LaboratoryWorkstationAudit、ExperimentInstanceAudit、ExperimentSurfaceProbe | 检查台面、实例、坐标及表面信息。 |
| ExperimentDuplicateInstanceCleanup、UncontrolledApparatusCleanup | 清理特定旧场景中的重复或未受控实例；不作为日常操作入口。 |
| StreamManagerPicoMigration、PicoBuildMenu、OpenPicoMainSceneOnLoad | PICO 迁移、构建和编辑器打开场景辅助。 |
| CreateWebCamera、CreateWebUserRig | 早期网页相机和用户代理创建工具。 |
| 各 Verification、SmokeTest、DebugWindow | 有针对性的安装后检查、逻辑验证与调试工具，名称不代表每个版本都已重新通过真机测试。 |

## 8. 回滚与验证范围

本次修改前备份位于 `MigrationBackups/BeforeTwoLevelLayout_20260910_204956_265`，包含 Unity 控制脚本、编辑器脚本、场景、项目设置、Packages，以及网页 viewer。291 个原文件均经过 SHA-256 对照校验。

目录内的回滚脚本和变更清单用于撤回本次实际修改；后续有人修改过的文件会被拒绝覆盖。安装器另行备份磁盘场景，并在当前场景有未保存修改时保存副本。恢复操作前应退出 Unity，以免内存场景覆盖已还原的磁盘文件。

在项目目录运行以下命令，仅核验备份与当前文件，不执行回滚：

```powershell
& './MigrationBackups/BeforeTwoLevelLayout_20260910_204956_265/rollback.ps1'
```

关闭 Unity 后，在上述命令末尾加 `-Restore` 执行回滚；加 `-Restore -WhatIf` 预览动作。`changed-files.json` 记录原始与修改后的 SHA-256，`After/` 另保留本次修改后的副本。

本次已完成：Unity C# 编译；19/31 数量、ID、台面支撑、间距、共享位置和原有任务参数检查；低高档各三个方向截图检查；Play Mode 中的 XRI 抓取释放、切换锁定、任务重置、目录刷新与布局日志检查；网页两档控件及旧场景四选项兼容检查。运行时验证使用合成 XRI 输入，不是手部追踪测试。报告位于 `Temp/PairedLayout/verification.txt` 与 `Temp/PairedLayout/runtime-verification.txt`。

默认保存为 Low。Unity 编辑器可在 `Tools > Stream Manager > Two Level Layout` 切换；网页连接更新后的 Unity 后会显示低/高两档。设备需要重新构建和安装 APK 才能获得本次 Unity 改动，浏览器需刷新加载新版 `main.js`。本次未构建 APK，也未验证真实串流联机。

编辑器配置和自动化交互检查不能替代 PICO 双手操作与真实串流验证。第一版只有一组配对布局 A；多个模板和研究方法对照仍属于后续工作。
