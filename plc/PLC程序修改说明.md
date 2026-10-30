# PLC 程序修改说明（上位机下发库位）

> 适用工程：`J24001 - 国赛2.0.pcwex`（PLCnext Engineer，EPC 1502）。
> 改动目标：识别结果由 WPF 上位机通过 **OPC UA** 写给 PLC，**库位也由上位机决定**。PLC 只负责搬运。
> 交互原理和新旧流程对比见 `docs/上位机/PLC交互说明.md`。

## 0. 先备份

在 PLCnext Engineer 里"另存为"一个新工程（例如 `J24001 - 上位机版.pcwex`），在副本上改，原工程不动。
本目录里还有两份原文件：
- `CHAIN_原版.st`：原来的 CHAIN.st，改坏了可以对照恢复；
- `CHAIN_修改后.st`：改好的整份 CHAIN.st，改动处都标了 `[修改]`。

## 1. 新增 2 个全局变量

在 PLCnext 节点的全局变量表（和 `Rsc_xExecute` 同一张表）里新增：

| 名称 | 类型 | 初值 | 勾选 | 说明 |
|---|---|---|---|---|
| `PC_Category` | INT | 0 | **OPC** | 上位机写的类别码：1 Q、2 H、3 K、4 W、5 NG；0 = 还没有结果 |
| `PC_TargetSlot` | INT | 0 | **OPC** | 上位机写的目标库位：1~12；0 = 没有可用库位，需人工取件 |

不要勾 Retain（掉电不需要保持，第 1 步会清 0）。

## 2. 给已有的 3 个变量勾选 OPC

上位机要通过 OPC UA 读写它们，没勾 OPC 的变量在 OPC UA 里看不到：

| 变量 | 位置 | 上位机怎么用 |
|---|---|---|
| `Rsc_xExecute` | 全局变量 | 读：工位上有工件（PLC 程序最后一行让它等于 `E_Detection`） |
| `tSTEP` | 全局变量 | 读：自动流程步号（第 7 步 = 等待识别结果，第 40 步 = 报警） |
| `MatState` | MainChain 的局部变量（VAR RETAIN） | 读：哪些库位有料；人工出库后写 0 |

上位机用到的 OPC UA 路径（在 `src/DiscSorter.Core/Plc/PlcTags.cs` 里，实例名不一样时改那里）：

```
Arp.Plc.Eclr/Rsc_xExecute
Arp.Plc.Eclr/tSTEP
Arp.Plc.Eclr/MainChain1.PC_Category     ← 实际建在 MainChain 的局部变量里（2026-10-30 现场工程），也可以用
Arp.Plc.Eclr/MainChain1.PC_TargetSlot
Arp.Plc.Eclr/MainChain1.MatState      ← MainChain 的程序实例名是 MainChain1（在任务 TASK1 里）
```

## 3. 改 CHAIN.st（4 处）

可以用 `CHAIN_修改后.st` 的内容整份替换 MainChain 的 CHAIN 代码页；也可以照下面只改 4 处。

**第 1 步**：清上位机上一件的结果。在 `//image_result.index := 0;` 下面加两行：

```
        PC_Category := 0;       // [修改] 清掉上位机上一件的结果（类别码）
        PC_TargetSlot := 0;     // [修改] 清掉上位机上一件的结果（目标库位）
```

**第 7 步**：原来等 `Camera.Data > 0`（Python 写的 `image_result.index`），改成等 `PC_Category`：

```
    7:  // [修改] 等上位机的识别结果。上位机先写 PC_TargetSlot，最后写 PC_Category，
        //        所以 PC_Category 不为 0 时，目标库位一定已经写好了
        IF PC_Category > 0 THEN
            iType := PC_Category;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF

        IF T1.Q THEN    // 5 秒没有结果：回第 1 步重来（工件还在，会再请求一次）
            EA_Chain.Step := 1;
        END_IF

        Message := Tips(0, "等待拍照结果");
```

**第 8 步**：删掉原来按类别写死分列的整个 `CASE iType OF ... END_CASE`（K→1~3、H→4~6、W→7~9、Q→10~12），换成：

```
    8:  // [修改] 库位由上位机决定
        IF PC_TargetSlot >= 1 AND PC_TargetSlot <= 12 THEN
            IF MatState[PC_TargetSlot] <> PC_TargetSlot THEN
                Pos := PC_TargetSlot;
                EA_Chain.Step := EA_Chain.Step + 1;
            ELSE
                EA_Chain.Step := 40;    // 库位冲突：上位机给的库位，PLC 记录为有料
            END_IF
        ELSE
            EA_Chain.Step := 40;        // 库位 0：没有可用库位，人工取件
        END_IF
```

（分成两层 IF 是为了先判断范围再访问数组。ST 的 AND 不保证短路求值，写在一个 IF 里，PC_TargetSlot = 0 时也可能去访问 MatState[0]。）

**第 40 步**：只改提示文字：

```
        Message := Tips(0, "无可用库位或库位冲突\n请人工取走工件后按复位");
```

**（可选，现在的 `CHAIN_修改后.st` 已包含）原位单独成组**：原来初始化时借用了别的点位的坐标作为原位：Y 用取料位 `ServoPos[2][0]`，X 用库位 10 的 `ServoPos[1][10]`，Z 用库位 2 的 `ServoPos[3][2]`。现在改成 `ServoPos[1][13]`、`[2][13]`、`[3][13]`（初始化第 2、4、5 步和自动第 26 步），坐标值和原来一样（X = -15200、Y = 2500、Z = -6500）。同时要把全局变量 `ServoPos` 的初值换成 `ServoPos_初值.txt` 里的那一行。以后调原位只改下标 13 这一项，不会影响取料位和库位。

其余步骤（取料、放料、第 24 步写 `MatState[Pos] := Pos`）都不用改。`imageClassify` 程序也不用动：它还在把 `image_result.index` 抄到 `Camera.Data`，只是现在没人用了。

## 4. OPC UA 服务器设置

在 PLCnext Engineer 的 PLC 设置（OPC UA 一页，名称以你的软件版本为准）里确认：
- **变量可见性**选"标记的变量"（Marked）：只有勾了 OPC 的变量才可见，上面已经勾好了 5 个。
- **安全**：2026-10-30 实测实验室这台 EPC 1502（固件 24.0.3）的 OPC UA 服务器开放了 None / Sign / SignAndEncrypt 三种模式，但**只接受匿名登录**。所以上位机"用户名"留空即可，"加密连接"勾不勾都能连上。如果以后在 PLC 上开启了用户认证，再填 WBM 的用户名和密码。
- **实测现状**：改程序之前，`Arp.Plc.Eclr` 下没有任何变量可见，所有路径都返回 `BadNodeIdUnknown`。这说明 OPC UA 的可见性是"只显示标记的变量"，而工程里还没有变量勾 OPC。按第 1、2 节勾好、重新下载后，这些变量才会出现。
- **证书**：第一次连接时上位机会自动生成自己的证书，存放在 `%LOCALAPPDATA%\DiscSorter\pki\own\certs`。如果 PLC 设置成只信任列表里的证书，要在 WBM 的"安全 → 证书"里把它加入 OPC UA 的信任列表（或者把信任模式设成"信任全部"，实验室内网可以这样做）。

改完后**编译并下载**到 PLC。

## 5. 停掉 EPC 上旧的 Python 容器

旧系统的 `edge-defect-app` 容器跑在 EPC 上。它会**独占相机**，并且还会写 `image_result`，必须停掉，否则上位机打不开相机。在 EPC 上执行：

```
sudo podman ps                      # 看容器名
sudo podman stop edge-defect-app
sudo podman update --restart=no edge-defect-app   # 防止开机自启（或按你当时的部署方式禁用）
```

## 6. 上位机这边

1. "PLC 通讯"页选"真实 PLC（OPC UA）"，**重启程序**。
2. 填地址 `opc.tcp://192.168.3.99:4840`、用户名、密码，勾"加密连接"，点"保存并连接"。
3. 变量表里能看到 5 个变量的值（例如 `tSTEP` 是当前步号）就说明通了。
4. 打开"相机"页，枚举并打开海康相机，图像来源选"海康相机"。
5. 到"监控"页点**联机**。PLC 的启动、停止仍然用现场按钮。

## 7. 现场调试顺序（建议）

1. 先不放工件，看 `tSTEP` 是否停在 2，`Rsc_xExecute` 是否为 0。
2. 放一个工件：`Rsc_xExecute` 变 1，`tSTEP` 经过 3、6 到 7。上位机日志出现"请求检测"，写 `PC_TargetSlot`、`PC_Category`。
3. PLC 进入第 8 步、第 10 步，开始搬运。放好后 `MatState[库位]` 变成库位号，上位机日志出现"放置完成"，仓储页的这个库位变成"已占用"。
4. 故意让某一列放满：上位机给库位 0，PLC 进第 40 步报警。人工取走工件，按复位按钮，PLC 回到第 1 步。
5. 在仓储页把一个库位"出库"：PLC 的 `MatState` 对应元素变成 0（"PLC 通讯"页的库位对照显示"一致"）。

## 8. 常见问题

| 现象（上位机提示） | 原因 | 处理 |
|---|---|---|
| `BadRequestTimeout`、连接超时 | 网络不通 | 本机网卡和 PLC 是否在 192.168.3.x 网段、网线、PLC 是否上电；先 `ping 192.168.3.99` |
| `BadUserAccessDenied` | 用户名或密码错误 | 用 WBM 能登录的账号 |
| `BadSecurityChecksFailed`、`BadCertificateUntrusted` | PLC 不信任上位机证书 | 按第 4 节在 WBM 里信任证书 |
| 读 `Arp.Plc.Eclr/...` 失败：`BadNodeIdUnknown` | 变量没勾 OPC，或程序实例名不是 MainChain1 | 检查第 1、2 节；实例名不同就改 `PlcTags.cs` |
| PLC 在第 7 步等 5 秒后回第 1 步，反复如此 | 上位机没有联机，不响应拍照请求 | 监控页点"联机"；"PLC 通讯"页的日志里应该出现"请求检测" |
| 相机打不开 | 被旧容器或 MVS 客户端占用 | 停掉旧容器（第 5 节），关掉 MVS 客户端 |

## 9. 回退

想恢复比赛时的状态：PLC 下载原工程；在 EPC 上重新启动旧容器即可。上位机不影响原 PLC 程序。
