# AGENTS.md — stream manager

> 本文档面向 AI Coding Agent。如果你对该项目一无所知，请从本节开始阅读。

---

## 一、项目概述

本项目是一个基于 **Unity 2022.3.62f3c1 (LTS)** 的 PC-VR 非对称交互研究系统，主要用于研究“面向 VR 用户的空间引导提示方法”。

- **VR 端**：Meta Quest 设备运行 Android APK，在本地渲染 VR 画面。
- **PC 端**：通过浏览器打开 `viewer` 网页，借助 **WebRTC RenderStreaming** 实时观看 VR 视角画面，并可通过鼠标点击向 VR 场景发送目标指令。
- **实验场景**：Unity 实验室静态场景（`3D Laboratory Environment with Appratus`），VR 用户可自由旋转头部，场景中分布有目标物体与干扰物。

项目根目录已包含预编译产物：
- `renderstream.apk` — Quest 安装包（约 90 MB，2026-04-15 生成）
- `webserver.exe` — WebSocket 信令服务器（约 39 MB，2026-01-06 生成）

---

## 二、技术栈

| 层级 | 技术 / 包 | 版本 / 说明 |
|------|-----------|-------------|
| 引擎 | Unity | 2022.3.62f3c1 (LTS) |
| 脚本 | C# | .NET Standard 2.1 (`LangVersion: 9.0`) |
| VR SDK | Meta XR SDK All | 83.0.1（含 `OVRCameraRig`、Interaction、Platform 等） |
| XR 运行时 | Unity OpenXR | 1.14.3，加载器为 `OpenXRLoader` |
| 流式传输 | Unity RenderStreaming | 3.1.0-exp.7（WebRTC 视频流 + 远程输入） |
| 输入 | Unity Input System | 1.14.0（新输入系统） |
| 信令服务器 | `webserver.exe` | 内置于项目根目录，默认端口 80，WebSocket 协议 |
| 渲染管线 | Built-in Render Pipeline | Forward Rendering，Linear Color Space |
| 构建目标 | Android | Min SDK 32 / Target SDK 32，APK 输出 |

其他依赖包（部分）：
- `com.unity.animation.rigging` 1.2.1
- `com.unity.textmeshpro` 3.0.7
- `com.unity.timeline` 1.7.7
- `com.unity.visualscripting` 1.9.4
- `com.unity.collab-proxy` 2.7.1

---

## 三、项目结构

```
stream manager/
├── Assets/
│   ├── 3D Laboratory Environment with Appratus/   # 实验室场景资产包
│   │   ├── Scenes/Laboratory Scene.unity          # ⭐ 主场景（Build 中已启用）
│   │   ├── Prefabs/                               # 烧杯、试管、桌椅等预制体
│   │   ├── Models/、Materials/、Textures/          # 模型与贴图
│   │   └── Documentation/                         # 资产包英文说明
│   ├── Editor/                                    # 编辑器扩展脚本
│   │   ├── CreateWebCamera.cs
│   │   ├── CreateWebUserRig.cs
│   │   └── SetupWebUserHands.cs
│   ├── Scenes/                                    # 其他场景（lab.unity、Receiver.unity 等）
│   ├── WebUser/                                   # 网页端用户化身相关 FBX / Animator
│   ├── XR/                                        # XR 通用设置与 OpenXR 配置资产
│   ├── Resources/                                 # 运行时资源（InputActions.asset 等）
│   ├── Samples/Unity Render Streaming/            # RenderStreaming 官方示例代码
│   ├── PlaterController.cs                        # PC 端射线选中逻辑（InputSystem 回调）
│   ├── OBject selector.cs                         # 网页端物体选择器（InputActionReference）
│   ├── NewBehaviourScript.cs                      # StreamManager（RenderStreaming 初始化）
│   ├── tagg.cs                                    # StreamCameraFollow（跟随 VR 相机）
│   └── WebHandInteractor.cs                       # 手部交互占位脚本
├── Packages/
│   ├── manifest.json                              # 包依赖清单
│   └── packages-lock.json                         # 锁定版本
├── ProjectSettings/                               # Unity 项目设置（Graphics、Quality、TagManager 等）
├── webserver.exe                                  # WebSocket 信令服务器可执行文件
├── renderstream.apk                               # Quest 预构建 APK
├── 架构说明.txt                                    # 中文架构说明（GBK 编码）
└── 设计方法.txt                                    # 研究设计与实验方法说明（GBK 编码）
```

> **注意**：本项目没有为主逻辑代码创建自定义 `Assembly Definition`（`.asmdef`），核心脚本全部落在默认的 `Assembly-CSharp` 程序集中。RenderStreaming 官方示例则使用了独立的 `.asmdef`。

---

## 四、运行时架构

### 4.1 摄像机架构

- 场景中仅保留 **VR 端摄像机**（`OVRCameraRig` → `TrackingSpace` → `CenterEyeAnchor`）。
- PC 端不再有独立渲染摄像机；`VideoStreamSender` 挂在 `CenterEyeAnchor` 上，将 VR 头显视角实时推流到浏览器。
- `PlayerController.renderStreamingCamera` 引用必须显式指向 `CenterEyeAnchor` 上的 `Camera`，**不可**使用 `Camera.main`（Quest 运行时 `Camera.main` 会指向 VR 摄像机，导致 RenderStreaming 射线检测逻辑异常）。

### 4.2 射线检测与选中逻辑

- PC 端（浏览器）的鼠标点击事件通过 RenderStreaming 的 `InputReceiver` 传入 Unity。
- `PlayerController.Click()` 将鼠标坐标按流分辨率（默认 1280×720）归一化为 Viewport 坐标，再通过 `renderStreamingCamera.ViewportPointToRay` 发射射线。
- 被射线击中的物体需满足：
  1. 带有 `Collider`；
  2. `Tag == "Interactable"`（目前 `TagManager` 中未预定义该标签，需在 Unity Editor 中手动添加）。
- 选中后，物体所有 `Renderer` 的材质颜色会被临时改为 `Color.yellow`；再次点击或取消则恢复原始颜色。

### 4.3 网络部署（局域网）

1. PC 与 Quest 连接至 **同一 WiFi**。
2. 在 PC 上启动信令服务器：`./webserver.exe`（默认端口 80，监听所有网卡）。
3. Quest 浏览器访问：`http://[PC 局域网 IP]:80/viewer/`
4. Unity 中 `RenderStreaming` 组件的 **Signaling URL** 设置为：`ws://[PC 局域网 IP]:80`

---

## 五、关键脚本说明

| 文件 | 类名 | 职责 |
|------|------|------|
| `PlaterController.cs` | `PlayerController` | 接收 InputSystem 输入（Look/Zoom/Move/Rotate/Click/Deselect），基于 `renderStreamingCamera` 做射线选中，并高亮目标。 |
| `OBject selector.cs` | `WebObjectSelector` | 通过 `InputActionReference` 获取点击与屏幕坐标，使用 `ScreenPointToRay` 检测 `Interactable` 标签物体，支持材质高亮替换。 |
| `NewBehaviourScript.cs` | `StreamManager` | 管理 `InputReceiver` 与 `VideoStreamSender`。启动时 `Application.runInBackground = true`，并根据视频流实际分辨率动态计算输入区域。 |
| `tagg.cs` | `StreamCameraFollow` | 使额外的 Stream Camera 每帧同步 VR 相机的位置、旋转与 FOV。 |
| `WebHandInteractor.cs` | `WebHandInteractor` | 占位组件，预留射线长度等字段，后续可扩展抓取、UI 交互等功能。 |
| `Editor/CreateWebCamera.cs` | `CreateWebCamera` | 菜单项 `Tools/Create Web Camera`，用于快速创建供 RenderStreaming 使用的独立 Camera。 |
| `Editor/CreateWebUserRig.cs` | `CreateWebUserRig` | 菜单项 `Tools/Create Web User Rig`，生成代表网页端用户的简易 Rig（Body/Head/Hands）。 |
| `Editor/SetupWebUserHands.cs` | `SetupWebUserHands` | 菜单项 `Tools/Setup WebUser Hands`，为 `LeftHand`/`RightHand` 添加 `RayOrigin`、`LineRenderer` 与 `WebHandInteractor`。 |

---

## 六、构建与部署流程

### 6.1 Unity 构建

- **平台**：切换至 Android（`File > Build Settings > Android`）。
- **目标设备**：Meta Quest（OpenXR + Meta Quest Touch 控制器配置）。
- **主场景**：确保 `Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity` 在 Build Settings 中唯一启用（当前配置如此）。
- **输出**：生成 `renderstream.apk` 或自定义名称的 APK。

### 6.2 运行

1. **PC 端**：
   ```powershell
   ./webserver.exe
   ```
   如需指定端口或 HTTPS：
   ```powershell
   ./webserver.exe -p 8080
   ./webserver.exe -s -p 443
   ```

2. **VR 端**：
   - 通过 SideQuest / ADB 安装 APK 到 Quest。
   - 或者在 Quest 浏览器中直接访问 PC 的 `viewer` 页面（若仅测试网页端逻辑）。

3. **IP 配置检查清单**：
   - [ ] PC 与 Quest 处于同一局域网。
   - [ ] `webserver.exe` 已运行且防火墙放行对应端口。
   - [ ] Unity 场景中的 `RenderStreaming` Signaling URL 填写为 PC 的实际局域网 IP（非 `127.0.0.1`）。
   - [ ] `PlayerController.renderStreamingCamera` 已正确赋值为 `CenterEyeAnchor` 上的 Camera。

---

## 七、代码风格指南

- **语言**：脚本内注释以中文为主，类名与公共 API 使用英文 PascalCase。
- **命名**：
  - 类 / 公共方法 / 公共字段：`PascalCase`
  - 私有字段：以下划线开头 `_camelCase`（如 `_selected`、`_originalColors`）
  - 常量 / SerializeField：混合使用，保持与现有文件一致即可。
- **结构**：遵循标准 Unity `MonoBehaviour` 模式，输入回调签名统一为 `public void OnXxx(InputAction.CallbackContext value)`。
- **序列化字段**：在 Inspector 中需要暴露的引用（如 `renderStreamingCamera`、`highlightMaterial`）应使用 `[Header]`、`[Tooltip]` 分组注释。
- **空检查**：对关键引用（`renderStreamingCamera`、`SelectedObject`）进行空值判断并输出 `Debug.LogWarning` / `Debug.LogError`。

---

## 八、已知问题与解决方案

| 问题 | 原因 | 解决方案 |
|------|------|----------|
| `Camera.main` 在 Quest 上无法用于 RenderStreaming 射线检测 | Quest 运行时 `Camera.main` 指向 VR 摄像机，而非流式摄像机 | 在脚本中显式赋值 `renderStreamingCamera` 字段，不要依赖 `Camera.main` |
| `FirstPersonLocomotor` 运行时强制对齐角色朝向到头显朝向，导致视角异常旋转或掉落 | OVR 初始化逻辑与角色控制器冲突 | 确保 `OVRPlayerController` 初始旋转为 `(0,0,0)`，或在 Inspector 中关闭 **HMD Rotates Y** 选项 |
| `Tag "Interactable"` 未定义 | `TagManager` 中无自定义标签 | 在 Unity Editor 的 `Edit > Project Settings > Tags and Layers` 中手动添加 `Interactable` 标签 |

---

## 九、测试说明

- 本项目目前**没有自动化单元测试或 PlayMode 测试**。测试依赖手动运行：
  1. **Editor PlayMode**：在 Unity 编辑器中直接运行主场景，验证 RenderStreaming 组件与 `PlayerController` 逻辑。
  2. **真机测试**：构建 APK 到 Quest，同时在 PC 启动 `webserver.exe`，在浏览器打开 `viewer` 页面进行端到端验证。
  3. **射线检测测试**：在 Scene 视图中确认目标物体带有 Collider 且 Tag 为 `Interactable`，点击后材质应变为黄色（`PlayerController`）或替换为 `highlightMaterial`（`WebObjectSelector`）。

---

## 十、安全与注意事项

- `webserver.exe` 默认监听所有网卡（`0.0.0.0`）的 80 端口。在公共网络中运行时，请评估是否需要通过防火墙限制访问范围，或启用 HTTPS（`-s` 参数并配置证书）。
- RenderStreaming 3.1.0-exp.7 仍为实验版本，API 可能在后续版本中发生破坏性变更。升级前需查阅官方 Migration Guide。
- `com.meta.xr.sdk.all` 83.0.1 与 Unity 2022.3 LTS 兼容，但升级 Unity 小版本前请确认 Meta XR SDK 的兼容性矩阵。
- 项目中存在预编译二进制文件（`webserver.exe`、`renderstream.apk`），提交到版本控制前请考虑是否需要在 `.gitignore` 中排除大体积二进制文件。

---

## 十一、外部参考

- `架构说明.txt` — 项目中文架构说明（GBK 编码），包含摄像机架构、射线逻辑、IP 配置。
- `设计方法.txt` — 研究设计说明（GBK 编码），包含实验条件、任务难度分级、评估指标。
- Unity RenderStreaming 文档：`Library/PackageCache/com.unity.renderstreaming@3.1.0-exp.7/Documentation~/`
- Meta XR SDK 文档：通过 Unity Package Manager 查看 `com.meta.xr.sdk.all` 的 README。
