# -*- coding: utf-8 -*-
"""pages_core.py — 总览 / 时钟 / GPIO / 中断
注意：f-string 体内只放原生 HTML；组件调用仅限单行参数；代码块用三单引号。"""

from gen_core import (page, sec, p, ul, table, rw, regtable, bits, code,
                      callout, grid, kpis, fig, Svg, esc)


# ============================================================ index
def build_index():
    s = Svg(940, 560)
    s.box(370, 30, 200, 70, ["pi32v2 DSP 内核", "最高 240 MHz"], "cpu", fs=14)
    s.box(300, 165, 340, 44, ["HSB 高速总线 (0x1F0000)"], "bus", fs=13)
    s.box(140, 165, 120, 44, ["LSB 低速总线", "(0x1E0000)"], "bus", fs=12)
    s.line(470, 100, 470, 165)
    s.line(300, 187, 260, 187)
    for x in (105, 255, 405, 555, 705):
        s.line(x, 209, x, 280, dashed=True)
    s.box(40, 280, 130, 60, ["SFC", "片内 Flash 控制"], "mem", fs=13)
    s.box(190, 280, 130, 60, ["PSRAM 控制器", "(选配)"], "mem", fs=13)
    s.box(340, 280, 130, 60, ["EQ / SRC", "音频加速"], "peri", fs=13)
    s.box(490, 280, 130, 60, ["FM 收发", "DSP"], "peri", fs=13)
    s.box(640, 280, 130, 60, ["DCP", "拷贝加速"], "peri", fs=13)
    row1 = [
        (40, "CLOCK / RST", "时钟与复位"), (190, "TIMER0-3", "定时器"),
        (340, "UART0-2", "串口"), (490, "SPI0-2", "SPI"),
        (640, "IIC", "I²C"), (790, "SD0 / SD1", "TF 卡"),
    ]
    for x, t1, t2 in row1:
        s.box(x, 415, 130, 56, [t1, t2], "peri", fs=12.5)
        s.line(x + 65, 209, x + 65, 415, dashed=True)
    row2 = [
        (40, "PORTA-D", "GPIO"), (190, "AUDIO/ANA", "音频模拟"),
        (340, "USB", "USB FS"), (490, "MCPWM", "PWM ×8"),
        (640, "ADC", "SARADC"), (790, "P33 / PMU", "电源管理"),
    ]
    for x, t1, t2 in row2:
        kind = "ana" if ("ANA" in t1 or "P33" in t1) else "peri"
        s.box(x, 495, 130, 56, [t1, t2], kind, fs=12.5)
        s.line(x + 65, 209, x + 65, 495, dashed=True)
    s.box(640, 165, 130, 44, ["CRC / AES / DMA", "加速与安全"], "peri", fs=11.5)
    s.line(705, 187, 705, 165)

    body = ""
    body += sec("intro", "芯片简介", f"""
<p>AC6956C（SDK 内部平台代号 <b>BR23</b>）是杰理科技的蓝牙音频 SoC，内核为杰理自主的
<b>pi32v2</b> 32 位 DSP（头文件中亦称 q32DSP），面向蓝牙音箱、学习机、故事机等音频产品。
本手册全部内容依据 SDK 头文件（<code>include_lib/driver/cpu/br23/asm/</code> 等）逆向整理，
寄存器地址与位域均可从 SDK 源码复核。</p>
{kpis([("pi32v2", "32 位 DSP 内核"), ("240 MHz", "最高系统主频（PLL480M÷2）"),
       ("1 MB", "片内 NOR Flash（SFC）"), ("3.0-3.6 V", "VDDIO 工作电压（可配）")])}
<p><b>主要特性：</b></p>
<ul>
<li>片内 NOR Flash 直接代码执行（XIP），支持 1/2/4 线模式与 eFuse 密钥加密启动</li>
<li>五路 PLL 输出（480/320/192/137/107 MHz），HSB/LSB/SFC 独立分频</li>
<li>3×UART、3×SPI、1×硬件 I²C、4×32 位定时器、8 通道 MCPWM</li>
<li>全差分 SARADC（16 通道含 PMU/音频内部通道），硬件 Audio DAC/ADC + FIFO</li>
<li>USB Full-Speed 设备控制器（5 组端点，SDK 内置 HID/CDC/MSC/AUDIO 类驱动）</li>
<li>双 SD/MMC 控制器（TF 卡）、RTC/PWR 掉电域（P33）、硬件 CRC16/AES</li>
</ul>
<div class="note"><b>命名说明：</b>SDK 中 <code>JL_XXX</code> 宏为各外设寄存器结构体指针，
定义于 <code>asm/br23.h</code>（总线外设）与 <code>asm/csfr.h</code>（内核 SFR）。
寄存器统一按 32 位访问——结构体中 <code>__u8/__u16</code> 均被宏替换为
<code>unsigned int</code>。</div>
""")

    body += sec("arch", "系统架构框图", f"""
{fig(s.render(), "图 1-1  AC6956C 总线架构与外设概览（虚线 = 总线连接）")}
<p>系统由两条内部总线组成：<b>HSB 高速总线</b>（基址 0x1F0000）挂接 SFC、PSRAM、
音频加速器等高带宽模块；<b>LSB 低速总线</b>（基址 0x1E0000）挂接全部常规外设。
内核 SFR（中断控制、MMU、调试模块等）位于 0x100000 起始的独立空间，定义见
<code>csfr.h</code>。</p>
""")

    addrrows = [
        ("HSB", "0x1F0000", "SFC", "0x02", "<code>0x1F0200</code>", "SPI Flash 控制器（片内 NOR）"),
        ("HSB", "0x1F0000", "SFCENC", "0x03", "<code>0x1F0300</code>", "Flash 加密单元"),
        ("HSB", "0x1F0000", "PSRAM", "0x05", "<code>0x1F0500</code>", "PSRAM 控制器"),
        ("HSB", "0x1F0000", "DCP", "0x13", "<code>0x1F1300</code>", "数据拷贝加速"),
        ("HSB", "0x1F0000", "EQ", "0x15", "<code>0x1F1500</code>", "硬件 EQ"),
        ("HSB", "0x1F0000", "SRC", "0x16", "<code>0x1F1600</code>", "采样率转换"),
        ("HSB", "0x1F0000", "FM", "0x17", "<code>0x1F1700</code>", "FM 收发 DSP"),
        ("LSB", "0x1E0000", "CLOCK", "0x00", "<code>0x1E0000</code>", "时钟/PLL/复位"),
        ("LSB", "0x1E0000", "TIMER0-3", "0x04-07", "<code>0x1E0400</code>+", "32 位定时器"),
        ("LSB", "0x1E0000", "PCNT / GPCNT", "0x10/11", "<code>0x1E1000</code>+", "脉冲计数器"),
        ("LSB", "0x1E0000", "SD0 / SD1", "0x14/15", "<code>0x1E1400</code>+", "SD/MMC"),
        ("LSB", "0x1E0000", "USB", "0x18", "<code>0x1E1800</code>", "USB 设备控制器"),
        ("LSB", "0x1E0000", "ANA", "0x19", "<code>0x1E1900</code>", "模拟寄存器 (DAA/ADA/WLA)"),
        ("LSB", "0x1E0000", "SPI0-2", "0x1C-1E", "<code>0x1E1C00</code>+", "SPI 主/从"),
        ("LSB", "0x1E0000", "UART0-2", "0x20-22", "<code>0x1E2000</code>+", "串口（含 DMA 收发）"),
        ("LSB", "0x1E0000", "IIC", "0x24", "<code>0x1E2400</code>", "硬件 I²C"),
        ("LSB", "0x1E0000", "ALNK0/1", "0x2E/33", "<code>0x1E2E00</code>+", "数字音频接口 (IIS)"),
        ("LSB", "0x1E0000", "AUDIO", "0x2F", "<code>0x1E2F00</code>", "Audio DAC/ADC FIFO"),
        ("LSB", "0x1E0000", "MCPWM", "0x30", "<code>0x1E3000</code>", "8 通道 PWM"),
        ("LSB", "0x1E0000", "ADC", "0x31", "<code>0x1E3100</code>", "SARADC"),
        ("LSB", "0x1E0000", "CRC", "0x35", "<code>0x1E3500</code>", "CRC16 加速"),
        ("LSB", "0x1E0000", "EFUSE", "0x37", "<code>0x1E3700</code>", "eFuse（ChipKey）"),
        ("LSB", "0x1E0000", "RAND", "0x3B", "<code>0x1E3B00</code>", "真随机数"),
        ("LSB", "0x1E0000", "P33", "0x3E", "<code>0x1E3E00</code>", "电源管理域接口"),
        ("LSB", "0x1E0000", "DMA", "0x3F", "<code>0x1E3F00</code>", "系统 DMA"),
        ("LSB", "0x1E0000", "SBC", "0x42", "<code>0x1E4200</code>", "SBC 编解码加速"),
        ("LSB", "0x1E0000", "AES", "0x43", "<code>0x1E4300</code>", "AES 加解密"),
        ("LSB", "0x1E0000", "PORTA-D", "0x50-53", "<code>0x1E5000</code>+", "GPIO（每端口 0x40）"),
        ("LSB", "0x1E0000", "IOMAP/WAKEUP", "0x51", "<code>0x1E5108</code>+", "IO 复用与唤醒"),
        ("LSB", "0x1E0000", "PLED / LCD", "0x52/53", "<code>0x1E5200</code>+", "LED 呼吸灯 / LCD 偏压"),
    ]
    body += sec("addrmap", "外设地址映射", f"""
<p>寄存器基址计算公式：<code>base = 段基址 + (64×grp + adr) × 4</code>
（<code>map_adr</code> 宏，grp 0x00-0xFF、adr 0x00-0x3F，组间距 256 字节）。</p>
{table(["段", "基址", "外设", "grp", "寄存器基址", "说明"], addrrows)}
""")

    cards = [
        ("⏱", "时钟系统", "PLL 五路输出、总线分频、外设时钟选择", "clock.html"),
        ("🔌", "GPIO 与 IO 复用", "4×16 GPIO、上下拉/驱动、输出通道映射", "gpio.html"),
        ("⚡", "中断系统", "64 个中断源、优先级、向量注册", "irq.html"),
        ("📡", "UART 串口", "3 路、DMA 环形收发、红外模式", "uart.html"),
        ("🔗", "SPI 总线", "3 组、1/2/4 线模式、主从", "spi.html"),
        ("🔗", "IIC 总线", "硬件 I²C 主从、完整位定义", "iic.html"),
        ("⏲", "定时器", "4×32 位定时器 + PWM 输出", "timer.html"),
        ("📊", "MCPWM", "8 通道、6 时基、边沿/中心对齐", "mcpwm.html"),
        ("🎚", "ADC 采样", "16 通道 SARADC、PMU 内部通道", "adc.html"),
        ("🎵", "音频系统", "DAC/ADC FIFO、模拟寄存器、ALNK", "audio.html"),
        ("🔌", "USB 设备", "Full-Speed、5 端点、UTMI 寄存器", "usb.html"),
        ("💾", "SD / TF 卡", "双控制器、卡检测、DMA", "sdio.html"),
        ("🔏", "SPI Flash 控制器", "XIP、2/4 线、eFuse 加密", "sfc.html"),
        ("🔋", "电源管理", "P33 掉电域、充电、LVD、软关机", "power.html"),
        ("🚀", "DMA 与安全加速器", "系统 DMA、CRC16、AES、随机数", "dma-sec.html"),
        ("📦", "其他外设", "WDT/RTC/RDEC/PWM-LED 等", "misc.html"),
    ]
    body += sec("peris", "外设章节导航", grid(cards, "g4"))

    page("index.html", "REFERENCE MANUAL", "AC6956C（BR23）芯片参考手册",
         "杰理 pi32v2 音频 SoC —— 依据 SDK 头文件逆向整理的寄存器级参考手册："
         "涵盖时钟、GPIO、串口、SPI/I²C、定时器、PWM、ADC、音频、USB、SD 卡、"
         "Flash 控制器、电源管理与安全加速器。", body)


# ============================================================ clock
def build_clock():
    s = Svg(920, 520)
    s.box(30, 40, 150, 56, ["BTOSC 晶振", "12-26 MHz"], "ana")
    s.box(30, 150, 150, 56, ["内部 RC", "250K / 16M"], "ana")
    s.box(250, 40, 170, 80, ["PLL", "480/320/192/137/107 M", "五路输出"], "cpu", fs=12.5)
    s.arrow(180, 68, 250, 68, "REF")
    s.arrow(180, 178, 240, 110)
    s.box(490, 40, 190, 56, ["PLL_SYS_SEL / DIV", "CLK_CON2[1:0]/[5:2]"], "bus", fs=12)
    s.arrow(420, 68, 490, 68)
    s.box(730, 40, 150, 56, ["HSB 时钟", "SYS_DIV[7:0]"], "cpu")
    s.arrow(680, 68, 730, 68)
    s.box(730, 130, 150, 56, ["LSB 时钟", "SYS_DIV[10:8]"], "bus")
    s.line(805, 96, 805, 130)
    s.box(730, 220, 150, 56, ["SFC 时钟", "SYS_DIV[14:12]"], "mem")
    s.line(805, 186, 805, 220)
    s.box(250, 190, 190, 180, ["外设时钟 CLK_CON1", "USB   [1:0]  PLL48M",
                               "AUDIO [3:2]  PLL48M", "UART  [11:10] PLL48M",
                               "BT    [15:14]", "WL2ADC/DAC"], "peri", fs=11.5)
    s.arrow(440, 90, 345, 190)
    s.box(250, 410, 190, 70, ["CLK_CON0", "主时钟源选择 [8:6]", "时钟输出 PA0/PB8"], "bus", fs=12)
    s.line(335, 68, 335, 150, dashed=True)
    s.arrow(335, 150, 335, 410, "主时钟 MUX")

    body = ""
    body += sec("tree", "时钟树", f"""
<p>AC6956C 的时钟体系为 <b>振荡源 → PLL → 总线分频 → 外设时钟</b> 四级结构。
系统主时钟可由 RC、BTOSC（12-26 MHz 晶振）、RTOSCL 或 PLL 提供（CLK_CON0[8:6]），
SDK 默认使用 BTOSC×PLL 方案；本项目配置为 PLL480M ÷ 2 = <b>240 MHz</b>
（<code>TCFG_CLOCK_SYS_HZ</code>）。</p>
{fig(s.render(), "图 2-1  时钟树与外设时钟选择")}
<div class="tip">SDK 屏蔽了寄存器细节：应用层用 <code>clk_early_init()</code> /
<code>clk_set()</code> / <code>sys_clk_set()</code>（clock.h）。直接改寄存器前务必先注册
<code>CLOCK_CRITICAL_HANDLE_REG</code> 临界区回调，否则频率切换会撕裂蓝牙时序。</div>
""")

    body += sec("regs", "寄存器总表（JL_CLOCK @ 0x1E0000）", regtable([
        (0x00, "PWR_CON", "RW", "电源控制（SDK 上电默认配置，勿动）"),
        (0x04, "HTC_CON", "RW", "HTC 控制"),
        (0x08, "SYS_DIV", "RW", "HSB/LSB/SFC 分频"),
        (0x0C, "CLK_CON0", "RW", "主时钟源选择、RC 振荡、时钟输出"),
        (0x10, "CLK_CON1", "RW", "外设时钟源选择（USB/AUDIO/UART/BT…）"),
        (0x14, "CLK_CON2", "RW", "PLL 系统时钟选择与分频"),
        (0x18, "CLK_CON3", "RW", "内核复位控制（SDK 保留）"),
        (0x40, "PLL_CON", "RW", "PLL 主控制"),
        (0x44, "PLL_CON1", "RW", "PLL 分频/输出使能/LDO"),
        (0x48, "PLL_INTF", "RW", "PLL 接口"),
        (0x4C, "PLL_DMAX", "RW", "PLL 分频上限"),
        (0x50, "PLL_DMIN", "RW", "PLL 分频下限"),
        (0x54, "PLL_DSTP", "RW", "PLL 分频步进"),
    ]))

    body += sec("con0", "CLK_CON0（0x0C）主时钟控制", bits([
        ("[0]", "RC_EN", "内部 RC 振荡使能"),
        ("[1]", "RCH_EN", "RC 高频选择：0=250K，1=16M"),
        ("[3:2]", "TEST_SEL", "测试时钟选择：0=主时钟，1=PAT"),
        ("[5:4]", "OSC_CLOCK_IN", "0=BTOSC，1=XOSC_FSCK，2=RTOSCL，3=PAT"),
        ("[8:6]", "MAIN_CLOCK_SEL", "主时钟：0=RC，4=BTOSC，6=RTOSCL，7=PLL"),
        ("[9]", "SFR_MODE", "SFR 时钟：0=随空闲停，1=常开"),
        ("[12:10]", "PA0_CLOCK_OUT", "PA0 时钟输出选择"),
        ("[15:13]", "PB8_CLOCK_OUT", "PB8 时钟输出选择"),
    ]))

    body += sec("con1", "CLK_CON1（0x10）外设时钟源", bits([
        ("[1:0]", "USB_CLOCK_IN", "USB：0=PLL48M，1=OSC，2=LSB，3=禁止"),
        ("[3:2]", "AUDIO_CLOCK_IN", "音频：0=PLL48M，1=OSC，2=LSB，3=禁止"),
        ("[6:4]", "GPCNT_CLOCK_IN", "通用计数器时钟源"),
        ("[11:10]", "UART_CLOCK_IN", "UART：0=PLL48M，1=OSC，2=LSB，3=禁止"),
        ("[15:14]", "BT_CLOCK_IN", "蓝牙：0=PLL48M，1=HSB，2=LSB，3=禁止"),
        ("[17]", "SFC_SCKE", "SFC 时钟停使能"),
        ("[19:18]", "WL2ADC_CLOCK_IN", "0=PLL96M，1=HSB，2=LSB，3=禁止"),
        ("[22:21]", "WL2DAC_CLOCK_IN", "同上"),
        ("[29:28]", "SFC_CLOCK_DELAY", "SFC 时钟延迟"),
    ]))
    body += '<div class="note">串口/USB 的 <b>PLL48M</b> 是 PLL 的固定 48 MHz 分支，不随系统主频变化——波特率因此稳定，即使系统降频运行。</div>\n'

    body += sec("con2", "CLK_CON2（0x14）系统时钟与分频", bits([
        ("[1:0]", "PLL_SYS_SEL", "0=192M，1=137M，2=320M，3=480M"),
        ("[5:2]", "PLL_SYS_DIV", "系统分频（编码见下）"),
        ("[6]", "PLL_ALNK_EN", "ALNK 时钟使能"),
        ("[7]", "PLL_ALNK_SEL", "0=192M/17，1=480M/39"),
        ("[13:12]", "PLL_FM_SEL", "FM 时钟：0=192M，1=137M，2=107M，3=禁止"),
        ("[17:14]", "PLL_FM_DIV", "FM 分频"),
        ("[30]", "DPLL_UDEN", "DPLL 更新使能"),
    ]))
    body += p("PLL_SYS_DIV 编码（与 SDK 枚举一致）：0→÷1，1→÷3，2→÷5，3→÷7；"
              "4-7→÷(1/3/5/7)×2；8-11→÷(1/3/5/7)×4；12-15→÷(1/3/5/7)×8。"
              "240 MHz = 480M 选 ÷1×2（编码 4）。")

    body += sec("sysdiv", "SYS_DIV（0x08）总线分频", bits([
        ("[7:0]", "HSB_CLK_DIV", "HSB 分频（0 = 直通；240M 配置在此）"),
        ("[10:8]", "LSB_CLK_DIV", "LSB 分频"),
        ("[14:12]", "SFC_CLK_DIV", "SFC 分频"),
    ]))

    body += sec("pll", "PLL_CON（0x40）/ PLL_CON1（0x44）", bits([
        ("CON[0]", "PLL_EN", "PLL 使能"),
        ("CON[1]", "PLL_REST", "PLL 复位"),
        ("CON[8:2]", "PLL_DIVn", "PLL 分频系数 n"),
        ("CON[9]", "PLL_REF_SEL", "参考时钟：0=BTOSC，1=RCLK"),
        ("CON[17:16]", "PLL_RSEL", "参考选择"),
        ("CON[23:22]", "PLL_PFD", "PFD 电流"),
        ("CON[26:24]", "PLL_ICP", "电荷泵电流"),
        ("CON1[11:0]", "PLL_FBDS", "反馈分频 DS"),
        ("CON1[14:12]", "PLL_IVCO", "VCO 电流"),
        ("CON1[23:22]", "PLL_DIVn_EN", "0=×2，1=直通，2=使能 2-33 分频"),
        ("CON1[24-28]", "CLK nM_OE", "480/320/192/137/107M 各路输出使能"),
        ("CON1[30]", "PLL_DAC_OE", "DAC 专用时钟输出使能"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/clock.h"

/* 1. SDK 标准流程：板级配置声明系统主频（board_config.h） */
#define TCFG_CLOCK_SYS_HZ   240000000     /* PLL480M / 2 */
#define TCFG_CLOCK_OSC_HZ   24000000      /* BTOSC 晶振频率 */

/* 2. 运行时降频（如进省电模式）：24M / 48M 两档 */
sys_clk_set(SYS_24M);
sys_clk_set(SYS_48M);

/* 3. 常用 API（asm/clock.h） */
clk_get("sys");                          /* 查询当前频率 */
clk_set("lsb", 8000000);                 /* 设定外设总线频率 */
clock_dump();                            /* 打印时钟树状态（调试） */
clock_set_sfc_max_freq(40000000, 80000000);

/* 4. 把时钟引到引脚观察（测量主频/PLL） */
PA0_CLOCK_OUT(PA0_CLOCK_OUT_HSB);        /* PA0 输出 HSB 时钟 */
PB8_CLOCK_OUT(PB8_CLOCK_OUT_PLL320);     /* PB8 输出 PLL320M */'''))
    body += '<div class="warn">本机（AC6956C8）实测：240 MHz 已是 PLL480M÷2 的规格上限，不要尝试 480M 直驱（超规格不可启动）。</div>\n'

    page("clock.html", "CHAPTER 2", "时钟系统（CLK / PLL）",
         "振荡源选择、PLL 五路输出、HSB/LSB/SFC 总线分频与外设时钟多路选择——"
         "对应 JL_CLOCK 寄存器组（0x1E0000）。", body)


# ============================================================ gpio
def build_gpio():
    s = Svg(900, 380)
    s.box(30, 30, 180, 300, ["端口寄存器", "OUT   0x00", "IN    0x04", "DIR   0x08",
                             "DIE   0x0C", "PU    0x10", "PD    0x14",
                             "HD0   0x18", "HD    0x1C", "DIEH  0x20"],
          "bus", fs=13)
    s.box(330, 40, 180, 70, ["输出驱动", "OUT + DIR"], "peri")
    s.box(330, 140, 180, 70, ["输入缓冲", "IN + DIE(输入使能)"], "peri")
    s.box(330, 240, 180, 70, ["上下拉 / 驱动", "PU / PD / HD0 / HD"], "peri")
    s.box(640, 130, 200, 90, ["引脚 PAD", "PA0-15 / PB0-15", "PC0-15 / PD0-7"], "dark", fs=13)
    s.arrow(210, 75, 330, 75)
    s.arrow(210, 175, 330, 175)
    s.arrow(210, 275, 330, 275)
    s.arrow(510, 75, 640, 155)
    s.arrow(640, 175, 510, 175)
    s.arrow(510, 275, 640, 195)

    s2 = Svg(900, 300)
    s2.box(20, 110, 170, 60, ["内部信号", "UART_TX / PWM / CLK…"], "peri", fs=12)
    s2.box(280, 40, 160, 56, ["输出通道 CH0", "IOMAP"], "bus", fs=12.5)
    s2.box(280, 180, 160, 56, ["输出通道 CH1", "IOMAP"], "bus", fs=12.5)
    s2.box(540, 40, 150, 56, ["任意引脚", "（输出使能）"], "peri", fs=12.5)
    s2.box(540, 180, 150, 56, ["任意引脚", "（输出使能）"], "peri", fs=12.5)
    s2.arrow(190, 140, 280, 68)
    s2.arrow(190, 140, 280, 208)
    s2.arrow(440, 68, 540, 68)
    s2.arrow(440, 208, 540, 208)

    body = ""
    body += sec("model", "GPIO 模型", f"""
<p>AC6956C 提供 <b>PA0-PA15 / PB0-PB15 / PC0-PC15 / PD0-PD7</b> 共 56 个 GPIO，
4 个端口组寄存器连续排列（每组 0x40 字节），每一位对应一个引脚。</p>
{fig(s.render(), "图 3-1  端口寄存器模型（每位对应一个引脚）")}
<div class="note">输入缓冲由 <b>DIE</b> 位单独控制（数字输入使能）；读 IN 前必须置 DIE=1。
模拟功能（ADC/触摸）引脚需 DIE=0 以降低数字干扰。</div>
""")
    body += sec("regs", "寄存器（JL_PORTA @ 0x1E5000，B/C/D 依次 +0x40）", regtable([
        (0x00, "OUT", "RW", "输出数据。DIR=1 时引脚电平 = OUT 位"),
        (0x04, "IN", "RO", "输入数据（需先置 DIE）"),
        (0x08, "DIR", "RW", "方向：1=输出，0=输入"),
        (0x0C, "DIE", "RW", "数字输入使能"),
        (0x10, "PU", "RW", "内部上拉使能"),
        (0x14, "PD", "RW", "内部下拉使能"),
        (0x18, "HD0", "RW", "驱动能力 0 档"),
        (0x1C, "HD", "RW", "驱动能力（大电流档）"),
        (0x20, "DIEH", "RW", "输入使能扩展（高速档）"),
    ]))

    body += sec("mux", "IO 复用与输出通道映射", f"""
<p>外设信号到引脚没有传统"复用选择器"，而是通过 <b>IOMAP 输出通道</b>路由：
内部信号（UART TX、PWM、时钟输出等）进入 CH0/CH1/CH2 三个输出通道（各 16 选 1），
再绑定到任意被使能为输出的引脚。</p>
{fig(s2.render(), "图 3-2  输出通道路由（CH0-CH2 → 任意引脚）")}
{table(["通道", "可选内部信号（gpio.h 枚举）"], [
 ("<code>CH0</code>", "<code>UT0_TX UT1_TX T0_PWM_OUT T1_PWM_OUT RTOSH_CLK BTOSC_CLK PLL_12M UT2_TX CH0-2_PWM_H/L WLC_BT_FREQ T3_PWM_OUT</code>"),
 ("<code>CH1</code>", "<code>UT0_TX UT1_TX T0_PWM_OUT WLC_BT_PRO RTOSL_CLK BTOSC_CLK SPDIF_DO UT2_TX CH0-2_PWM_H/L T2_PWM_OUT T3_PWM_OUT</code>"),
 ("<code>CH2</code>", "<code>UT1_RTS UT1_TX WLC_BT_ACTIVE T1_PWM_OUT PLNK_SCLK BTOSC_CLK PLL_24M UT2_TX CH0-2_PWM_H/L T2_PWM_OUT T3_PWM_OUT</code>"),
])}
<p>时钟输出可直接经 CLK_CON0 固定路由到 <b>PA0</b> 与 <b>PB8</b>（见时钟章节）。</p>
""")

    body += sec("ad", "模拟复用（ADC 通道）", table(["通道宏", "引脚/信号"], [
        ("<code>AD_CH_PA1 / PA5 / PA6 / PA10 / PA12</code>", "PA 组模拟引脚"),
        ("<code>AD_CH_PB1 / PB3 / PB4 / PB8 / PB10</code>", "PB 组模拟引脚（PB1 = AD 按键常用）"),
        ("<code>AD_CH_PC4 / PC5 / PC6</code>", "PC 组模拟引脚"),
        ("<code>AD_CH_DP</code>", "USB DP（可做 ADC）"),
        ("<code>AD_CH_RTC</code>", "RTC 域信号"),
        ("<code>AD_CH_PMU_*</code>", "内部：VBAT(÷4)、LDO5V(÷4)、VDC13、DTEMP、BG 基准…"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/gpio.h"

/* 输出：POW_FLS 按键电源轨（本机实例） */
gpio_direction_output(IO_PORTB_11, 1);      /* 方向输出 + 输出 1 */

/* 输入 + 上拉（AD 按键） */
gpio_set_direction(IO_PORTB_01, 0);         /* 0 = 输入 */
gpio_set_pull_up(IO_PORTB_01, 1);           /* 22K 上拉（外部） */
gpio_set_die(IO_PORTB_01, 1);               /* 打开数字输入（读 IN 必需） */
int v = gpio_read(IO_PORTB_01);

/* 直接写端口寄存器（位操作） */
JL_PORTB->OUT |=  BIT(11);   /* PB11 = 1 */
JL_PORTB->OUT &= ~BIT(11);   /* PB11 = 0 */
JL_PORTB->DIR |=  BIT(11);   /* 输出 */

/* 关闭引脚（低功耗：浮空并关输入缓冲） */
gpio_close_uart0();          /* 释放调试串口引脚 */'''))
    body += '<div class="tip">引脚编号宏为 <code>IO_PORTx_NN = 16×组 + 序号</code>，因此 API 里的 u32 gpio 可直接做数组下标换算。</div>\n'

    body += sec("wakeup", "唤醒与中断", p(
        "引脚电平变化经 <b>JL_WAKEUP</b>（0x1E5108，CON0-CON3）产生 <code>IRQ_PORT_IDX</code>"
        "（中断号 13）端口中断；软关机后由 P33 域的独立唤醒检测支持按键开机。"
        "SDK 的按键驱动（AD 键/IO 键/触摸）都构建在该机制之上。"))

    page("gpio.html", "CHAPTER 3", "GPIO 与 IO 复用",
         "4 组 56 个 GPIO：方向/上下拉/驱动能力寄存器、数字输入使能、"
         "IOMAP 输出通道路由、ADC 模拟复用与唤醒——对应 JL_PORTA-D / IOMAP / WAKEUP。", body)


# ============================================================ irq
def build_irq():
    rows = [
        ("0", "IRQ_EMUEXCPT_IDX", "仿真异常"), ("1", "IRQ_EXCEPTION_IDX", "异常"),
        ("2", "IRQ_SYSCALL_IDX", "系统调用"), ("3", "IRQ_TICK_TMR_IDX", "系统 TICK"),
        ("4", "IRQ_TIME0_IDX", "定时器 0"), ("5", "IRQ_TIME1_IDX", "定时器 1"),
        ("6", "IRQ_TIME2_IDX", "定时器 2"), ("7", "IRQ_TIME3_IDX", "定时器 3"),
        ("8", "IRQ_USB_SOF_IDX", "USB 帧起始"), ("9", "IRQ_USB_CTRL_IDX", "USB 控制/数据"),
        ("10", "IRQ_RTC_WDT_IDX", "RTC / WDT"), ("11", "IRQ_ALINK0_IDX", "ALNK0 音频链路"),
        ("12", "IRQ_AUDIO_IDX", "Audio FIFO"), ("13", "IRQ_PORT_IDX", "GPIO 电平变化"),
        ("14", "IRQ_SPI0_IDX", "SPI0"), ("15", "IRQ_SPI1_IDX", "SPI1"),
        ("16", "IRQ_SD0_IDX", "SD0"), ("17", "IRQ_SD1_IDX", "SD1"),
        ("18", "IRQ_UART0_IDX", "UART0"), ("19", "IRQ_UART1_IDX", "UART1"),
        ("20", "IRQ_UART2_IDX", "UART2"), ("21", "IRQ_PAP_IDX", "并口 PAP"),
        ("22", "IRQ_IIC_IDX", "硬件 I²C"), ("23", "IRQ_SARADC_IDX", "SARADC"),
        ("24", "IRQ_PDM_LINK_IDX", "PDM 链路"), ("25", "IRQ_RDEC0_IDX", "旋转解码 0"),
        ("26", "IRQ_LRCT_IDX", "低功耗计数"), ("27", "IRQ_BREDR_IDX", "蓝牙 BR/EDR"),
        ("28", "IRQ_BT_CLKN_IDX", "蓝牙时钟"), ("29", "IRQ_BT_DBG_IDX", "蓝牙调试"),
        ("30", "IRQ_WL_LOFC_IDX", "无线 LOFC"), ("31", "IRQ_SRC_IDX", "SRC 采样率转换"),
        ("32", "IRQ_FFT_IDX", "FFT 加速"), ("33", "IRQ_EQ_IDX", "硬件 EQ"),
        ("34", "IRQ_LP_TIMER0_IDX", "低功耗定时器 0"), ("35", "IRQ_LP_TIMER1_IDX", "低功耗定时器 1"),
        ("36", "IRQ_ALINK1_IDX", "ALNK1 音频链路"), ("37", "IRQ_OSA_IDX", "OSA"),
        ("38", "IRQ_BLE_RX_IDX", "BLE RX"), ("39", "IRQ_BLE_EVENT_IDX", "BLE 事件"),
        ("40", "IRQ_AES_IDX", "AES 加速"), ("41", "IRQ_MCTMRX_IDX", "MC 定时器"),
        ("42", "IRQ_CHX_PWM_IDX", "MCPWM 通道"), ("43", "IRQ_FMRX_IDX", "FM 接收"),
        ("44", "IRQ_SPI2_IDX", "SPI2"), ("45", "IRQ_SBC_IDX", "SBC 编解码"),
        ("46", "IRQ_GPC_IDX", "GPCNT"), ("47", "IRQ_FMTX_IDX", "FM 发射"),
        ("48", "IRQ_DCP_IDX", "DCP 拷贝"), ("49", "IRQ_RDEC1_IDX", "旋转解码 1"),
        ("50", "IRQ_RDEC2_IDX", "旋转解码 2"), ("51", "IRQ_SPDIF_IDX", "SPDIF"),
        ("52", "IRQ_PWM_LED_IDX", "PWM 呼吸灯"), ("53", "IRQ_CTM_IDX", "CTM 触摸"),
        ("60-63", "IRQ_SOFT0-3_IDX", "软件中断"),
    ]
    body = ""
    body += sec("model", "中断模型", f"""
<p>pi32v2 内核支持 <b>64 个中断源</b>（向量表基址 <code>IRQ_MEM_ADDR = 0x2BF00</code>），
每个源可注册回调并指定优先级。SDK 封装了注册/开关接口，驱动里中断服务函数用
<code>___interrupt</code> 属性修饰。</p>
<div class="note">hwi.h 中每个中断号后的注释（0/1/2）为 SDK 建议的优先级档位：
数字越大优先级越高，蓝牙时序相关（27/28/30/38）建议保持 2 级。</div>
""")
    body += sec("tbl", "中断源总表", table(["#", "宏（hwi.h）", "外设 / 说明"], rows))

    body += sec("use", "使用方法", code(
'''#include "asm/hwi.h"
#include "asm/irq.h"

/* 1. 注册中断：优先级 0-3，cpu_id 固定填 0 */
static ___interrupt void my_spi2_isr(void)
{
    if (JL_SPI2->CON & BIT(4)) {        /* pending? */
        /* ... 读取数据 ... */
        spi_clear_pending(SPI2);
    }
}

request_irq(IRQ_SPI2_IDX, 1, my_spi2_isr, 0);

/* 2. 开关中断（掩码操作） */
irq_disable(IRQ_SPI2_IDX);
irq_enable(IRQ_SPI2_IDX);

/* 3. 卸载 */
unrequest_irq(IRQ_SPI2_IDX);

/* 4. 临界区（关闭全局中断） */
local_irq_disable();
/* ... */
local_irq_enable();'''))
    body += '<div class="warn">ISR 中不得调用阻塞 API（如 fopen/printf 长打印）；与主循环共享数据用 <code>local_irq_disable()</code> 保护或做成环形缓冲。SDK 的 UART/SPI 中断收发已封装好，一般无需自写 ISR。</div>\n'

    page("irq.html", "CHAPTER 4", "中断系统",
         "pi32v2 内核 64 源中断控制器：中断号总表、优先级、向量注册与 ISR 编写规范。", body)
