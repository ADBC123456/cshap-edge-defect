# 基于 HALCON 与 WPF 的圆片工件缺陷识别与分类入库系统

本科毕业设计项目。海康工业相机拍摄圆片工件，**HALCON** 识别缺陷类别（缺角 Q、划痕 H、孔洞 K、污渍 W），**WPF 上位机**按"列—类别"映射分配库位，通过 **OPC UA** 把类别和目标库位发给菲尼克斯 PLCnext（EPC 1502），由三轴机械臂放入对应库位，并记录入库信息。

```
海康相机 ──GigE──▶ WPF 上位机（DiscSorter） ──OPC UA──▶ PLCnext EPC 1502 ──▶ 三轴机械臂 → 4×3 立体库位
                    ├ HALCON 识别（HDevEngine 调 DiscDefect.hdev）
                    ├ 仓储管理（SQLite：列映射、库位、任务、记录）
                    └ 握手流程（拍照请求 → 下发类别和库位 → 放置完成确认）
```

## 功能

| 页面 | 内容 |
|---|---|
| 监控 | 运行控制（启动/停止/单件/复位，真实 PLC 时为联机/脱机）、最新一件的图像和结果、库位图、今日统计、最近记录、流程日志 |
| 视觉调试 | 加载 HALCON 程序、打开图片或从相机取图、图层显示（圆、分割、候选、缺陷）、三图并排、参数调节与保存、特征表 |
| 仓储管理 | 4 列 × 3 行库位图，每列指定一个类别（Q/H/K/W/NG 不重复），出库、整列出库、待确认库位处理、入库任务和操作日志 |
| 相机 | 枚举局域网海康相机（MVS SDK）、打开/关闭、曝光增益、拍一张并识别、自动运行的图像来源（相机 / 图片文件夹）、图片保存 |
| PLC 通讯 | 仿真 PLC / 真实 PLC（OPC UA）切换与连接、5 个交换变量的实时值、库位对照（上位机 vs PLC 的 MatState）、通讯日志 |
| 设置 | 入库类别来源（算法 / 文件名真值）、回放图片文件夹、仿真 PLC 节拍与故障注入、文件路径 |

类别码：**1 Q 缺角、2 H 划痕、3 K 孔洞、4 W 污渍、5 NG 异常**。库位号 = (列 − 1) × 3 + 行。某类别没有分配列或该列已满时下发库位 0，PLC 报警等待人工取件。

## 目录结构

```
DiscSorter.sln
├─ src/DiscSorter.App        WPF 界面（CommunityToolkit.Mvvm），6 个页面
├─ src/DiscSorter.Core       业务逻辑，不含界面
│   ├─ Models/               类别、库位、任务、记录等数据类
│   ├─ Data/                 SQLite 建库升级、仓储服务、记录、设置
│   ├─ Vision/               HALCON 调用（HalconDetector）、参数、图片文件夹、海康相机（HikCamera）
│   ├─ Plc/                  握手换算（StationPlcBase）、仿真 PLC、OPC UA 客户端（OpcUaPlc）、变量路径（PlcTags）
│   ├─ Workflow/             分拣协调器（SortingCoordinator）
│   └─ Common/               工程路径、密码加密（DPAPI）
├─ tests/DiscSorter.Tests    xUnit 测试（64 个）
├─ halcon/                   识别算法 DiscDefect.hdev、参数说明 detector_meta.json
├─ plc/                      PLC 程序修改：CHAIN_修改后.st、CHAIN_原版.st、ServoPos_初值.txt、修改说明
├─ lib/MVS/                  海康 MVS SDK 的 .NET 接口（MvCameraControl.Net.dll）
├─ data/                     回归测试图片：station（工位截图）、camera（真机图）、samples、synthetic
├─ docs/                     文档（见下）
├─ tools/                    OpenCV 基线对比脚本（Python）
├─ J24001.pcwex              PLCnext Engineer 工程
└─ 毕设实施计划书.md
```

## 运行环境

| 软件 | 版本 / 说明 |
|---|---|
| Windows + Visual Studio 2022 | .NET 8 SDK，项目平台 x64 |
| HALCON | 23.11 Progress（需要有效许可证），NuGet `MVTec.HalconDotNet` 23110.0.0 |
| 海康 MVS | 4.x 客户端或 Runtime（提供底层 MvCameraControl.dll）；只用图片文件夹时可以不装 |
| PLC | 菲尼克斯 EPC 1502（PLCnext，固件 24.0.3），OPC UA 端口 4840；没有 PLC 时用仿真 PLC |
| 其他 NuGet | CommunityToolkit.Mvvm 8.4.2、Microsoft.Data.Sqlite 8.0.29、OPCFoundation.NetStandard.Opc.Ua.Client 1.5.378 |

## 快速开始

1. 用 Visual Studio 打开 `DiscSorter.sln`，启动项目选 **DiscSorter.App**，按 F5。
2. 第一次运行会在 `%LOCALAPPDATA%\DiscSorter\Data\discsorter.db` 建数据库。默认使用**仿真 PLC** 和 `data\station` 图片回放，不需要任何硬件。
3. 在监控页点"启动"，就能看到完整的"拍照—识别—分配库位—入库"流程；"PLC 通讯"页可以看到每一步交换了哪些变量。

命令行测试：

```bash
dotnet test DiscSorter.sln
```

HALCON 算法批量回归（结果写到 `output/`）：

```bash
hrun -b halcon/DiscDefect.hdev
```

## 识别算法（halcon/DiscDefect.hdev）

上位机通过 HDevEngine 调用其中的 4 个过程：`dd_default_params`、`dd_locate_disk`、`dd_segment`、`dd_detect`（接口见 `docs/上位机/HALCON过程接口约定.md`）。

1. **转灰度**：取 RGB 最大值（V 通道），减弱偏蓝的阴影。
2. **找圆**：Canny 边缘 → 圆弧拟合出候选圆 → metrology 逐个测量打分，取得分最高的圆。
3. **分割**：局部均值背景 → 暗度（两种模式，见下）→ 双阈值（滞后阈值）→ 闭运算 → 连通域。
4. **特征与分类**（按顺序）：对比度不够 → 不算缺陷；碰边且切出角 → **Q**；孔比例大 → **K**；厚度大 → **W**；细长 → **H**。主缺陷取面积 × 对比度最大的一个。

暗度模式（参数 `darkness_mode`，在视觉调试页修改并保存）：

| 模式 | 含义 | 截图 12 张 | 真机 6 张 |
|---|---|---|---|
| `absolute`（默认） | 比背景暗多少灰度，光照均匀时用 | 12/12 | 2/6 |
| `relative` | 比背景暗百分之几，圆片上有重阴影时用 | 12/12 | 6/6 |

工位截图上的完整测试：12/12，平均 43 ms；稳健性测试（旋转、镜像、缩放、变暗、加噪声，共 96 张）96/96。OpenCV 基线为 11/12，平均 446 ms。原理、每个算子、阈值来源见 `docs/halcon算法/算法讲解.md`。

> **已知问题**：工位目前没有专用光源（相机曝光约 63 ms，靠室内灯光），圆片上会出现较重的斜阴影。缺陷压在阴影分界线上时可能误判。建议加装环形光源，把曝光降到 5～10 ms；改善光照之前可以先用 `relative` 模式。

## 与 PLC 的交互

上位机与 PLC 只交换 5 个变量（OPC UA，匿名登录，命名空间 `http://phoenixcontact.com/OpcUA/PLCnext/GlobalDataSpace/`）：

| 变量 | OPC UA 路径 | 方向 | 含义 |
|---|---|---|---|
| `Rsc_xExecute` | `Arp.Plc.Eclr/Rsc_xExecute` | PLC → 上位机 | 工位上有工件 |
| `tSTEP` | `Arp.Plc.Eclr/tSTEP` | PLC → 上位机 | 自动流程步号（7 = 等待识别结果，40 = 报警） |
| `PC_Category` | `Arp.Plc.Eclr/MainChain1.PC_Category` | 上位机 → PLC | 类别码，**最后写** |
| `PC_TargetSlot` | `Arp.Plc.Eclr/MainChain1.PC_TargetSlot` | 上位机 → PLC | 目标库位 1～12，0 = 人工取件 |
| `MatState` | `Arp.Plc.Eclr/MainChain1.MatState` | 双向 | 库位状态，`MatState[i] = i` 表示有料 |

流程：PLC 第 7 步等结果 → 上位机拍照识别、分配库位，**先写库位、再写类别** → PLC 第 8 步校验库位后搬运 → 第 24 步写 `MatState[库位]` → 上位机看到变化后确认入库。人工出库后，上位机把对应的 `MatState` 清 0。

PLC 程序只改了 `CHAIN.st` 的第 1、7、8、40 步，另外让初始化原位单独使用 `ServoPos[轴][13]`，见 `plc/PLC程序修改说明.md`。

## 文档

| 文档 | 内容 |
|---|---|
| `docs/上位机/代码导读.md` | 界面截图、项目结构、程序运行流程、MVVM 写法、建议阅读顺序 |
| `docs/上位机/PLC交互说明.md` | 旧系统和新系统的交互流程、变量表、时序、异常处理 |
| `docs/上位机/HALCON过程接口约定.md` | 上位机调用 HALCON 过程的签名和 Result 字典格式 |
| `docs/halcon算法/算法讲解.md` | 识别算法的原理、算子、参数、阈值来源、测试结果 |
| `plc/PLC程序修改说明.md` | PLC 工程的修改步骤、OPC UA 设置、现场调试顺序、常见问题 |
| `毕设实施计划书.md` | 总体方案与进度计划 |

## 代码风格

为方便阅读和答辩，C# 代码统一使用教材级写法：一个文件一个类，中文注释；不用 LINQ、lambda、async/await 等写法；所有逻辑都在界面线程上，由 50 ms 定时器驱动（PLC 走一步 → 协调器处理 → 刷新界面）。XAML 页面带设计时示例数据，可以在 Visual Studio 设计器里直接预览和修改布局，颜色和样式集中在 `Styles/Styles.xaml`。
