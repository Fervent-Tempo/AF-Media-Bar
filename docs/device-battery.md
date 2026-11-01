# 设备电量开发与验收

本分支增加 USB／2.4G 接收器电量、紧凑多设备概览及可独立排序的多设备组件。原有蓝牙设置字段、组件 ID、实验开关及低电量阈值保持兼容。

## 参考来源与首批范围

| 来源 | 固定参考版本 | 本次用途 |
|---|---|---|
| [Voltide](https://github.com/sdparklab/Voltide/tree/73f16fd2be44570c0a861cef6b4804d743656893) | `73f16fd`，MIT，Joko；ATK 贡献者 yiwluoq | 七类 HID 适配器的查询和响应协议 |
| [InphicBatteryTray](https://github.com/Tan-TanZi/InphicBatteryTray/tree/86c8cd08eaae74966799a840e9a8d45ca1a6cd37) | `86c8cd0`，MIT，Tan-TanZi | IN10 被动报告、IN9 Feature 查询 |
| [FerrisPulse](https://github.com/Ferris-echo/FerrisPulse/tree/4fcda58d8166d32423dc1175f0110b567f728975) | `4fcda58`，GPL-3.0 | 调研设备类型；本次未引入其源码或驱动脚本 |

IN9 协议的上游实机依据是 [InphicBatteryTray-IN9](https://github.com/MRXie-zf/InphicBatteryTray-IN9)，由 InphicBatteryTray 文档引用。复用来源的许可见 [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md)。

| 适配器 | 发现条件／目标 | 读数方式 |
|---|---|---|
| 标准蓝牙 | 已配对并连接的设备 | 优先未缓存 BLE Battery Service；Windows 属性按已知时间戳择新，无时间戳的属性标记非实时 |
| 罗技 | `046D`，`FF00/0002`，20/20 字节集合 | 枚举 1–6 及直连 FF 槽位，只加入提供 `1004` 或 `1000` 电池特性的设备；名称优先来自设备 |
| 迈从协议族 | `3554:FA09`，`FF02/0002`，20/20 字节 | `13 4A` 查询，校验累加和；保留系统名称，不将共用标识的狼蛛设备强制命名为迈从 |
| ROG OMNI | `0B05:1ACE`，`FF00`，64/64 字节 | `02 12 01` 查询；充电状态未知，不推断为未充电 |
| 雷蛇鼠标 | Voltide 明确列出的接收器 PID，91 字节 Feature，优先厂商集合 | `07/80` 电量，0–255 转百分比；`07/84` 单独查询充电，校验事务和 CRC；普通鼠标集合也只查询 Feature |
| 雷蛇耳机 | BlackShark V2 Pro，`1532:0555`，`FF00`，64/64 字节 | PA/PI 帧，分别查询 `21` 电量及 `2A` 充电 |
| ATK 鼠标 | A9 Air 大师+，`373B:1278`，`FF02/0002`，17/17 字节 | 控制管道 Output Report `08 04`，校验和为 `4D` |
| ATK 耳机 | N9 V2 Ultimate，`373B:1262`，`FF90`，64/64 字节 | 中断管道 `55 65 01`，不访问相邻 `FF82` 集合 |
| INPHIC IN10 | 参考库九组 VID/PID，`000A`，5 字节输入 | 只读监听 `03 <type> 40 01 <percent>`；首批目标为单鼠标槽位 1，8 秒无有效报告即非实时／离线 |
| INPHIC IN9 | `248A/249A:5B2E/5B2F`，33 字节 Feature，排除键鼠输入集合 | `0C 01 F0` 握手与 `20` 查询，验证响应序号、有效及在线标志 |

这些是已实现的协议入口，**本分支全部型号仍待用户验收**。参考库的实测记录不能作为 AF Media Bar 已实机通过的证据。有线且不具备电池特性的型号不会因品牌匹配而生成电量。

## 运行策略

- 启用“设备电量”实验功能，在设置页勾选需要显示的设备，以上下移动按钮调整顺序，并在组件布局中启用电量组件。主设备单独用于提醒与概览标记；旧配置沿用原主设备，显式空列表表示不显示设备。
- 数值模式显示类型图标及百分比；图形模式在设备轮廓内向上填充。未知电量显示问号，充电与过期读数分别以闪电、时钟标记，完整信息放入 Tooltip。默认数值模式。
- 轮换每 5 秒切换，槽位宽度稳定，悬停、键盘聚焦及拖动期间暂停，不触发硬件读取。并列模式每台设备可独立拖至性能、音量等组件之间。两种模式分别保留布局；设置页顺序决定轮换及新增并列设备的初始顺序，断开或取消勾选不删除已保存位置。
- 左键或键盘 Enter／空格打开概览；设置页“查看全部设备”也可打开，便于主设备断开、组件隐藏时改选。右键仍提供刷新和设置。
- 概览显示时立即发现并读取受支持设备，之后每 60 秒刷新。点击设备即可设为主设备；Esc 或点击外部关闭。
- 面板关闭后只追踪“启用显示的勾选设备＋启用提醒的主设备”，释放其他会话。IN10 活动会话持续监听，其他协议成功后每分钟查询，失败逐步退避至最多十分钟。断开设备隐藏、重连恢复；连接但没有电量时显示未知，低电量显隐按设备分别判断。
- 失败、休眠或断开保留最后有效读数及原时间，不产生低电量提醒。Windows 属性没有硬件时间戳时显示非实时；未知充电状态保持未知。
- 普通用户权限，共享打开 HID 句柄；不读键盘按键／鼠标位移输入，不写 DPI、灯光或按键配置。不增加联网或遥测。
- 原生挂起请求取消后等待完成才释放内存；异常驱动若不完成取消，后台清理可能等待驱动，不能用超时后释放缓冲区掩盖它。

设备类型优先依据 Windows Bluetooth CoD／LE Appearance、HID 使用集合及明确协议型号。混合接收器与不明确分类保留通用图标，不靠名称猜测。分类依据：[Windows CoD 属性](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/properties/props-system-devices-aep-bluetooth-cod-major.md)、[LE Appearance 属性](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-aep-bluetooth-le-appearance)、[Bluetooth Assigned Numbers](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Assigned_Numbers/out/en/index-en.html)。

## 验收步骤

1. 退出当前旧版，从 `artifacts/battery-ui-review/AFMediaBar.exe` 启动。保留整个目录及其依赖，不能只复制框架依赖版 exe。
2. 启用实验，连接或唤醒设备，打开概览。分别对照厂商工具的电量；确认同接收器不同罗技槽位独立显示，设备名称不被误改。
3. 勾选多台设备，用键盘操作上下移动按钮；切换数值／图形、轮换／并列，检查图标、未知值、充电与过期标记。轮换悬停／聚焦暂停。更改主设备仅改变提醒对象及标记，不覆盖勾选列表。
4. 测试休眠／唤醒、接收器拔插、蓝牙断开／重连；IN10 停止上报超过 8 秒后应变为非实时，恢复上报后恢复。
5. 将并列设备分别拖到性能或音量组件两侧；Esc 撤销拖动，完成拖动不打开面板。往返切换轮换／并列、取消勾选／重新勾选及重启，检查各设备原位置保留。
6. 横竖布局、向两侧展开封面，检查每个可见电量表面同步避让、命中位置同步移动，收回后复位；展开期间让设备显隐或切换显示方式再检查。检查任务栏／悬浮模式、多屏缩放、浅色／深色／高对比度及四种语言。
7. 用 Tab、Enter、空格和 Esc 完成打开、选择与关闭。关闭概览后，仅勾选显示设备与提醒主设备继续追踪；关闭实验及退出时确认没有遗留浮窗或监听。

首次低电量读取、选择变化与重连只建立提醒基线；实时电量从阈值上方降到阈值时提醒一次，升至阈值加 5 后重新允许提醒。硬件、Windows 及弹窗生命周期均**待用户／维护者验收**。

## 自动检查

使用现有 MSTest 项目的 `DeviceBatteryTests`、`BatteryPresentationTests` 及 WPF 布局检查，覆盖报文校验、量程、多槽位隔离、陈旧读数、会话释放和迟到发布门禁，以及新增的设备身份、排序持久化、模式切换、旧设置回退、追踪集合与封面避让；不查询真实硬件。

```powershell
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:BuildInParallel=false -m:1
dotnet test .\tests\AFMediaBar.Layout.Tests\AFMediaBar.Layout.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~BatteryPresentationTests|FullyQualifiedName~DeviceBatteryTests|FullyQualifiedName~BatteryDevicesHaveIndependentSurfacesAndFollowArtworkPush|FullyQualifiedName~ZoomPushTakesTheSignOfTheResolvedExpansionDirection"
git diff --check
```

不运行全量回归、发布或安装程序；自动检查不替代上述实机验收。

本次最终结果（2026-11-02）：Release 构建通过，0 错误、8 个既有代码／依赖警告；最终专项 12 项全部通过（6 秒）。前一轮扩展检查为 25 项通过、1 项失败；失败项 `FractionalWidthRepeatedLayoutKeepsButtonsStableAndResetsDirection` 在独立基线 `0cc2477` 中重现相同差异：150% 缩放时预期 85.6、实际 86.3333，允许差值 0.6733。本轮未扩大范围修改该既有布局问题。仅在编译失败、控件测量修复和预览宿主尺寸调整后重跑相关检查，没有运行全量套件。

验收输出位于 `artifacts/battery-ui-review/`。其中 `build.log`、`tests.log`、`final-tests.log`、`baseline-tests.log` 保留结果；`screenshots/battery-*.png` 是真实编译后的 WPF 控件使用预览数据渲染的图片，包含浅／深色、紧凑概览、数值／图形、轮换／并列、横／竖向。图片采用 96／144 DPI 渲染，不等于真实显示器 DPI 或硬件验收。鼠标、键盘、耳机、手柄及未知电量均由模拟快照覆盖。

桌面自动化连接因本机通信管道不可用而失败，因此本轮使用隔离 WPF 窗口检查控件，没有启动第二个完整应用，也未退出用户正在运行的旧版。完整桌面生命周期、真实多屏 DPI、高对比度及硬件读数仍待用户验收。所有提交仅在本地，未推送或创建 PR。

## 后续批次

FerrisPulse 独有的雷柏、NuPhy、瓦尔基里、VGN/VXE、Sony、Apple/Beats、HyperX、赛睿、漫步者、宏碁及手柄私有协议不在首轮。需要进一步核实协议来源及硬件报文，不能通过名称或相同 VID 批量推断支持，也不移植 GPL 源码到本项目。
