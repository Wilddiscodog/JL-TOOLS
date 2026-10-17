# JL-TOOLS — 杰理 AC6956C（词易帮 Y20）工具与固件集

本仓库汇集 AC6956C8（BR23 平台，词易帮 Y20 学习机）的**自制烧录工具、
自编译固件与芯片参考手册**。芯片无官方公开手册，固件与手册均为依据
杰理 SDK（AC695N_Soundbox_SDK）逆向/开发所得，仅供学习与个人设备维护使用。

## 目录内容

```
├── firmware/       本次编译的固件（V0.0.3 电子书阅读器）
├── JlFlash/        自研 USB 烧录工具（C# / .NET 8，含源码与编译产物）
└── chip-manual/    AC6956C 芯片参考手册（17 页 HTML，图文版）
```

## 1. firmware — 固件

| 文件 | 用途 |
|---|---|
| `jl_isd.bin` | **直烧镜像**（JlFlash 使用这个），950272 字节 |
| `jl_isd.fw` | 升级固件（isd 格式） |
| `update.ufw` / `nor_update.ufw` | 卡升级 / NOR 升级包 |

**V0.0.4 固件功能**（2026-10-17 编译，md5 `ce8e72839787eaa6d5cbddafd2a53a0a`）：

- 电子书阅读器：TF 卡书柜（最近阅读置顶、进度百分比）、9 档字号
  （TF:\FONT\FONT12~28.BIN，宋体/黑体，缺字库自动回退内嵌 12px）、
  章节目录（第X章识别+TF 缓存）、书签（12 条含摘录）、进度跳转、
  标点悬挂/整词换行排版、多书进度独立记忆
- 关机时序修复：长按关机/菜单确认关机立即清屏显示 POWER OFF!
  （语音播报在其后进行），断电前兜底重画确保面板锁存关机帧
- 原 AURA TOUCH OS 全部功能保留（触摸画板/示波器/音乐/五子棋/塔罗牌等）
- 日志模式：按住任意键上电 → OTG 禁用，DP/DM 作为 UART0 串口（1M 波特率）

刷机后 TF 卡建议目录：

```
TF:\
├── EBOOK\*.TXT      UTF-8 编码的电子书
└── FONT\FONT12.BIN ... FONT28.BIN   可选，字号字库
```

## 2. JlFlash — 烧录工具

自研的杰理 USB 烧录器（Win10/11 x64，需 .NET 8 Runtime），含命令行与图形界面两个版本：

### JlFlash（命令行版，`bin-release/`）

- 自动识别 UDISK 模式 → 踢入 UBOOT → 擦写校验 → 复位，全流程一键完成
- 支持读回芯片 ChipKey（eFuse）、Flash ID

使用方法：

```
cd JlFlash/bin-release
dotnet JlFlash.exe            # 或直接双击 JlFlash.exe
```

烧录时选择 `firmware/jl_isd.bin`，设备插 USB 后按提示操作即可。

### JlFlashGui（图形界面版，`JlFlashGui/bin-release/`）

带窗口的操作界面，功能分区：

- **设备区**：扫描/踢 UBOOT 设备、显示设备信息
- **AC695N SRAM 运行区**：加载 payload 到指定 RAM 地址运行
  （地址/邮箱/参数/加密方式可选），用于裸机调试与loader实验
- **固件烧录区**：选择固件文件一键烧录 AC695N Flash

使用方法：

```
cd JlFlash/JlFlashGui/bin-release
JlFlashGui.exe                # 直接双击
```

## 3. chip-manual — 芯片参考手册

依据 SDK 头文件（`br23.h` / `clock_hw.h` / `iic_hw.h` / `hwi.h` 等）逆向整理的
寄存器级参考手册，17 个章节页面：

时钟系统、GPIO 与 IO 复用、中断系统、UART、SPI、I²C、定时器、MCPWM、
SARADC、音频系统、USB、SD/MMC、SPI Flash 控制器（XIP/eFuse 加密）、
电源管理（P33）、DMA 与安全加速器、其他外设。

双击 `chip-manual/index.html` 阅读；页面由 `chip-manual/build/` 下的
Python 生成器产出，可自行修改再生成。

## 免责声明

- 固件仅适用于 AC6956C8（词易帮 Y20）硬件，刷错型号有变砖风险
- 烧录有风险，操作前请自行备份原厂固件
- 所有内容仅供学习研究，勿用于商业用途
