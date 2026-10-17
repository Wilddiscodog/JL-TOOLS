# -*- coding: utf-8 -*-
"""pages_periph2.py — ADC / 音频 / USB / SDIO / SFC（f-string 体内只放原生 HTML）"""

from gen_core import (page, sec, p, ul, table, rw, regtable, bits, code,
                      callout, grid, kpis, fig, Svg, esc)


# ============================================================ adc
def build_adc():
    s = Svg(880, 380)
    s.box(30, 30, 190, 330, ["外部通道", "PA1  PA5  PA6  PA10", "PA12 PB1  PB3  PB4",
                             "PB8  PB10 PC4  PC5", "PC6  DP"], "peri", fs=12)
    s.box(280, 40, 180, 80, ["内部 MUX 通道", "PMU：VBAT/LDO5V/", "VDC13/DTEMP/BG…"], "ana", fs=12)
    s.box(280, 160, 180, 80, ["内部 MUX 通道", "AUDIO：VOUT/米克偏置", "PLL/FM/BT 监测"], "ana", fs=12)
    s.box(280, 280, 180, 60, ["RTC 通道", "AD_CH_RTC"], "ana", fs=12)
    s.box(560, 120, 150, 120, ["SARADC", "CON", "RES（结果）", "IRQ 23"], "cpu", fs=13)
    s.arrow(220, 180, 560, 180)
    s.arrow(460, 80, 560, 150)
    s.arrow(460, 200, 560, 180)
    s.arrow(460, 310, 560, 215)
    s.box(770, 130, 90, 100, ["软件", "轮询表", "adc_add_", "sample_ch()"], "bus", fs=12)

    body = ""
    body += sec("ov", "概述", f"""
<p>全差分 SARADC，<b>16 个输入通道</b>：14 个外部引脚复用 + RTC + 内部 MUX
（PMU 电压/温度、音频偏置、PLL/FM/BT 监测）。SDK 驱动维护一张最多 10 通道的
<b>轮询采样表</b>——调用 <code>adc_add_sample_ch()</code> 后硬件循环采样，
<code>adc_get_value()</code> 取最近值，适合按键分压、电池电压等低频场景；
需要精确单次测量用 <code>adc_enter_occupy_mode()</code> 独占模式。</p>
{fig(s.render(), "图 10-1  ADC 通道结构")}
""")
    body += sec("regs", "寄存器（JL_ADC @ 0x1E3100）", regtable([
        (0x00, "CON", "RW", "采样控制（通道选择/启动，SDK 驱动管理）"),
        (0x04, "RES", "RO", "采样结果（原始码值）"),
    ]))
    body += '<div class="note">控制位细节在闭源驱动内；结果需经 VBG 基准换算：<code>电压 = adc_value_to_voltage(vbg, val)</code>，SDK 自动用 PMU 带隙基准校准。</div>\n'

    body += sec("ch", "通道定义（adc_api.h）", table(["组", "宏", "说明"], [
        ("外部", "<code>AD_CH_PA1 PA5 PA6 PA10 PA12 PB1 PB3 PB4 PB8 PB10 PC4 PC5 PC6 DP</code>", "引脚复用，直接用引脚名"),
        ("RTC", "<code>AD_CH_RTC</code>", "RTC 域信号"),
        ("PMU", "<code>AD_CH_VBAT</code>（VBAT÷4）、<code>AD_CH_LDO5V</code>（÷4）、<code>AD_CH_VDC13 / SYSVDD / DTEMP / PMU_VBG / WVDD / PROGF</code>", "电源监测：电池电压、USB 输入、芯片温度"),
        ("AUDIO", "<code>AD_CH_F/R_VOUTL/R MICLDO CTADCREF DACVDD</code>", "音频模拟监测"),
        ("PLL/FM/BT", "<code>AD_CH_BIAS ANALOG DIGITAL 480MEG FMTX_SEL BT_SEL</code>", "射频/模拟监测"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/adc_api.h"

/* 1. 加入轮询表（内部会启动 ADC 循环采样） */
adc_add_sample_ch(AD_CH_PB1);        /* AD 按键 */
adc_add_sample_ch(AD_CH_VBAT);       /* 电池电压 */

/* 2. 读取原始值 / 换算电压 */
u32 raw = adc_get_value(AD_CH_PB1);
u32 mv  = adc_get_voltage(AD_CH_VBAT);

/* 3. 精确单次测量（独占模式，测完释放） */
adc_enter_occupy_mode(AD_CH_VDC13);
u32 v = adc_get_occupy_value();
adc_exit_occupy_mode();

/* 4. 电压换算辅助（用 VBG 基准校准） */
u32 volt = adc_value_to_voltage(adc_get_value(AD_CH_PMU_VBG), raw);'''))
    body += '<div class="tip">本机实践：AD 按键梯（PB1）上电采样前必须先拉高电源轨 PB11（POW_FLS），并确认 <code>adc_add_sample_ch(AD_CH_PB1)</code> 已注册——否则 adc_get_value 恒为 0。</div>\n'

    page("adc.html", "CHAPTER 10", "ADC 采样",
         "16 通道全差分 SARADC：外部引脚、PMU/音频内部通道、轮询采样表与独占模式——对应 JL_ADC（0x1E3100）。", body)


# ============================================================ audio
def build_audio():
    s = Svg(920, 400)
    s.box(30, 40, 170, 90, ["Audio DAC FIFO", "DAC_ADR / LEN", "DAC_PNS 采样率", "IRQ 12"], "peri", fs=12)
    s.box(30, 220, 170, 90, ["Audio ADC FIFO", "ADC_ADR / LEN", "ADC_PNS", "MIC 采集"], "peri", fs=12)
    s.box(300, 40, 180, 90, ["数字 ΔΣ 调制", "DAC_VL0/1 音量", "DAC_TM 插值"], "cpu", fs=12)
    s.box(300, 220, 180, 90, ["ADC 数字滤波", "ADC_SRP/SRN", "高通/陷波"], "cpu", fs=12)
    s.box(580, 40, 150, 90, ["DAA 模拟", "DAC 增益/静音", "ANA_CON[0xC0+]"], "ana", fs=12)
    s.box(580, 220, 150, 90, ["ADA 模拟", "MIC 偏置/增益", "ANA_CON[0xE0+]"], "ana", fs=12)
    s.box(800, 40, 100, 90, ["HPA /", "喇叭", "(AXS2005B)"], "dark", fs=12)
    s.box(800, 220, 100, 90, ["MIC", "偏置 LDO"], "dark", fs=12)
    s.arrow(200, 85, 300, 85)
    s.arrow(480, 85, 580, 85)
    s.arrow(730, 85, 800, 85)
    s.arrow(200, 265, 300, 265)
    s.arrow(480, 265, 580, 265)
    s.arrow(730, 265, 800, 265)
    s.box(40, 340, 300, 44, ["时钟：CLK_CON1[3:2] AUDIO_CLOCK_IN（默认 PLL48M）"], "bus", fs=11.5)

    body = ""
    body += sec("ov", "概述", f"""
<p>音频子系统由三部分组成：<b>JL_AUDIO</b>（DAC/ADC 的 DMA FIFO 与数字滤波）、
<b>JL_ANA</b> 中的 <b>DAA/ADA</b> 模拟寄存器组（增益/静音/偏置）、以及
<b>ALNK0/1</b> 数字音频链路（I²S/PCM，接外挂 codec）。
本机外挂 AXS2005B 功放（PB0 控制静音），DAC 输出经板级模拟通路接功放。</p>
{fig(s.render(), "图 11-1  音频通路")}
""")
    body += sec("regs", "寄存器（JL_AUDIO @ 0x1E2F00）", regtable([
        (0x00, "DAC_CON", "RW", "DAC FIFO 控制"),
        (0x04, "DAC_ADR", "RW", "DAC DMA 缓冲地址"),
        (0x08, "DAC_LEN", "RW", "DMA 长度"),
        (0x0C, "DAC_PNS", "RW", "采样率选择"),
        (0x10, "DAC_HRP", "RW", "写指针"),
        (0x14, "DAC_SWP/SWN", "RW", "状态/指针"),
        (0x20, "DAC_VL0 / VL1", "RW", "数字音量 L / R"),
        (0x28, "DAC_TM0 / TM1", "RW", "插值滤波"),
        (0x30, "DAC_DTV / DTB", "RW", "数字微调"),
        (0x40, "DAC_DPD", "WO", "深掉电控制"),
        (0x44, "DAC_COP", "WO", "时钟输出"),
        (0x48, "ADC_CON", "RW", "ADC FIFO 控制"),
        (0x4C, "ADC_ADR", "RW", "ADC DMA 缓冲地址"),
        (0x50, "ADC_LEN / PNS", "RW", "DMA 长度 / 采样率"),
        (0x58, "ADC_HWP", "RW", "读指针"),
        (0x5C, "ADC_SRP / SRN", "RW", "数字滤波"),
    ]))
    body += p("模拟部分在 <code>JL_ANA @ 0x1E1900</code>：<b>DAA_CON0-7</b>（0xC0 起，"
              "DAC 模拟：增益/静音/上电）、<b>ADA_CON0-4</b>（0xE0 起，MIC 偏置/增益），"
              "位定义由 SDK 电源/音频驱动（<code>p33.h</code> 宏、<code>audio_adc.h</code>）封装。")

    body += sec("alnk", "数字音频接口 ALNK0/1", p(
        "<code>JL_ALNK0 @ 0x1E2E00</code>、<code>JL_ALNK1 @ 0x1E3300</code>："
        "I²S/PCM 主从收发（CON0-3 + 4 组 DMA 描述符 ADR0-3 + LEN），"
        "时钟来自 PLL_ALNK（192M/17 或 480M/39 ≈ 11.29/12.31 MHz，对应 44.1/48k 族）。"
        "接外挂 codec 或蓝牙音频协处理器时使用；SDK 用 <code>iis.h</code> API 封装。"))

    body += sec("use", "使用方法", code(
'''/* 应用层走 SDK 框架（cpu/br23/audio_*.h），不直接碰寄存器 */
#include "asm/audio_adc.h"
#include "audio_dac.h"

/* 本机功放静音链（learning_board.c 实例）：
   音频活动回调 -> PB0 拉高使能 AXS2005B，空闲自动静音省电 */
learning_amp_mute(1);              /* 上电先静音 */
audio_dac_start(...);              /* 框架启动 DAC 通路 */

/* 采集 MIC（ADC 通路） */
audio_mic_ldo_en(0, &adc_data);    /* 本机关闭 MIC LDO 隔离声反馈 */'''))
    body += '<div class="warn">DAC 模拟上电顺序敏感（DAA 上电→放音→下电），务必通过 SDK 音频框架启停；直接写 ANA 寄存器可能出现爆音甚至损伤喇叭。</div>\n'

    page("audio.html", "CHAPTER 11", "音频系统",
         "Audio DAC/ADC DMA-FIFO、模拟增益链（DAA/ADA）与 ALNK 数字音频接口——对应 JL_AUDIO（0x1E2F00）、JL_ANA（0x1E1900）。", body)


# ============================================================ usb
def build_usb():
    s = Svg(900, 340)
    s.box(30, 60, 190, 80, ["DP / DM 引脚", "USB_IO CON0：", "上下拉、输出、中断", "（与 UART0 复用）"], "dark", fs=12)
    s.box(320, 40, 200, 120, ["USB SIE", "CON0：PHY/SIE 中断", "SOF / SIE pending",
                              "INDEX 选端点", "UTMI 级寄存器"], "cpu", fs=12.5)
    s.box(620, 30, 220, 70, ["EP0 控制", "8B 描述符 / EP0_CNT"], "peri", fs=12)
    s.box(620, 130, 220, 90, ["EP1-4 数据端点", "EPx_TADR / EPx_RADR", "EPx_CNT（双向）", "FIFO 64-256B"], "peri", fs=12)
    s.box(320, 230, 200, 70, ["OTG 检测", "otg_dev_data", "usb_dev_en"], "bus", fs=12)
    s.arrow(220, 100, 320, 100)
    s.arrow(520, 75, 620, 65)
    s.arrow(520, 110, 620, 160)
    s.arrow(420, 160, 420, 230)

    body = ""
    body += sec("ov", "概述", f"""
<p>USB <b>Full-Speed 设备控制器</b>（12 Mbps）+ OTG 检测：1 个控制端点 EP0 +
4 组双向数据端点。FIFO 大小按类配置（SDK 默认：EP0/HID/MSD 64B、CDC 128B、
AUDIO 256B）。设备控制器包含一套 <b>UTMI 风格的索引寄存器</b>
（FADDR/INDEX/TXCSR…），经 CON1 的主机访问接口读写。</p>
{kpis([("FS 12M", "Full-Speed"), ("EP0-4", "1 控制 + 4 数据"),
       ("HID/CDC/MSC/AUDIO", "SDK 内置类驱动"), ("IRQ 8/9", "SOF / 控制+数据")])}
{fig(s.render(), "图 12-1  USB 控制器结构")}
""")
    body += sec("regs", "寄存器", regtable([
        (0x00, "USB CON0", "RW", "PHY/中断/pending（位见下）"),
        (0x04, "USB CON1", "RW", "UTMI 索引访问（MACK 触发/RNW）"),
        (0x08, "EP0_CNT", "WO", "EP0 字节计数"),
        (0x0C, "EP1-4_CNT", "WO", "各端点计数（每端点 1 个）"),
        (0x1C, "EP0_ADR", "WO", "EP0 DMA 地址"),
        (0x20, "EP1-4_TADR/RADR", "WO", "各端点收/发 DMA 地址"),
    ]))
    body += sec("con0", "CON0 位名（usb.h，JL_USB @ 0x1E1800）", bits([
        ("[0]", "PHY_ON", "PHY 上电"),
        ("[1]", "LOW_SPEED", "低速模式"),
        ("[2]", "USB_NRST", "IP 复位"),
        ("[4]", "CID", "OTG 角色"),
        ("[5]", "VBUS", "（读）VBUS 状态"),
        ("[9]", "PDCHKDP", "DP 检测使能"),
        ("[10]", "SOFIE", "SOF 中断使能"),
        ("[11]", "SIEIE", "SIE 中断使能"),
        ("[12]", "CLR_SOF_PND", "清 SOF pending"),
        ("[13]", "SOF_PND", "（读）SOF 标志"),
        ("[14]", "SIE_PND", "（读）SIE 标志"),
        ("[15]", "CHKDPO", "DP 检查"),
        ("[17:16]", "DP_SE / DM_SE", "DP/DM 单端检测"),
    ]))
    body += sec("utmi", "UTMI 索引寄存器（经 INDEX 选择端点）", table(["偏移", "寄存器", "说明"], [
        ("0x00", "FADDR", "设备地址"),
        ("0x01", "POWER", "电源/挂起/复位"),
        ("0x02-0x0B", "INTRTX/RX/USB 及使能", "中断状态与使能"),
        ("0x0C", "FRAME1/2", "帧号"),
        ("0x0E", "INDEX", "选择当前端点"),
        ("0x0F", "DEVCTL", "设备控制"),
        ("0x10-0x1B", "TXMAXP/CSR/RXMAXP/CSR/COUNT/TYPE/INTERVAL", "端点配置与状态"),
    ]))

    body += sec("use", "使用方法", code(
'''/* SDK 场景化封装（通常无需碰寄存器） */
#include "device/usb/otg.h"

/* OTG 数据（board 板级） */
struct otg_dev_data otg_data = {
    .usb_dev_en = 1,             /* 作 USB 设备 */
};

/* 本机实践：日志模式下置 usb_dev_en=0，
   OTG 检测被禁用，DP/DM 保持 UART0 功能 */
otg_data.usb_dev_en = 0;

/* MSD 升级通道：接电脑后 UDISK 模式被 JlFlash 用于烧录 */'''))
    body += '<div class="warn">DP/DM 同时是 UART0 调试口的引脚——USB 枚举期间控制台会失效；需要全程日志时必须禁用 OTG（见上）。</div>\n'

    page("usb.html", "CHAPTER 12", "USB 设备控制器",
         "Full-Speed 设备 + OTG：SIE/PHY 控制、5 组端点 DMA 与 UTMI 索引寄存器——对应 JL_USB（0x1E1800）、JL_USB_IO（0x1E5100）。", body)


# ============================================================ sdio
def build_sdio():
    s = Svg(860, 280)
    s.box(30, 60, 190, 70, ["时钟 / 分频", "CON1"], "ana", fs=12)
    s.box(30, 170, 190, 70, ["命令通道", "CPTR：命令指针"], "peri", fs=12)
    s.box(320, 60, 200, 180, ["SD/MMC 核心", "CON0 控制", "CON2 状态", "DPTR 数据指针", "CTU 超时"], "cpu", fs=13)
    s.box(630, 90, 180, 120, ["SD0 / SD1", "4 位总线", "CMD / CLK", "卡检测", "IRQ 16 / 17"], "dark", fs=12.5)
    s.arrow(220, 95, 320, 110)
    s.arrow(220, 205, 320, 190)
    s.arrow(520, 150, 630, 150)

    body = ""
    body += sec("ov", "概述", f"""
<p>双 SD/MMC 控制器（SD0 / SD1），支持 SD 卡 1/4 位模式。SDK 的文件系统
（挂载为 <code>storage/sd0 / storage/sd1</code> 设备）构建其上。
<b>本机 TF 卡接在 SD1</b>（PC3-PC5 引脚组），电子书、字库均存于 TF。</p>
{fig(s.render(), "图 13-1  SD/MMC 控制器")}
""")
    body += sec("regs", "寄存器（JL_SD0 @ 0x1E1400，SD1 @ 0x1E1500）", regtable([
        (0x00, "CON0", "RW", "主控制"),
        (0x04, "CON1", "RW", "时钟分频/电气"),
        (0x08, "CON2", "RW", "状态/中断"),
        (0x0C, "CPTR", "WO", "命令指针（写入发送命令）"),
        (0x10, "DPTR", "WO", "数据指针（写入启动数据传输）"),
        (0x14, "CTU_CON", "RW", "超时控制"),
        (0x18, "CTU_CNT", "WO", "超时计数"),
    ]))
    body += '<div class="note">平台数据中 <code>.sfr = (volatile u16 *)JL_SD1</code>——SD 控制器部分寄存器按 16 位访问，与全芯片 32 位访问的惯例不同，移植驱动时注意。</div>\n'

    body += sec("use", "使用方法", code(
'''#include "asm/sdmmc.h"

/* 板级配置（本机 TF = SD1） */
SD1_PLATFORM_DATA_BEGIN(sd1_data);
    .port                   = 'C',          /* PC3-PC5 */
    .priority               = 3,
    .max_freq               = 25000000,
SD1_PLATFORM_END();

/* 电源与挂载（SDK 设备层自动完成） */
sd_set_power(1);                          /* TF 供电轨 */
dev_manager_online_check_by_logo("sd1", 0);  /* 在线检查 */

/* 文件系统访问（fscan/fopen 见 fs.h） */
eb_scan = fscan("storage/sd1/C/EBOOK/", "-tTXT -sn", 1);'''))
    body += '<div class="tip">TF 供电与按键梯共用 POW_FLS 电源轨（PB11）——<code>sd_set_power/sdpg_config</code> 会接管该轨，初始化顺序：先按键采样、后挂 TF。</div>\n'

    page("sdio.html", "CHAPTER 13", "SD / TF 卡控制器",
         "双 SD/MMC 主机：命令/数据通路、卡检测与 DMA——对应 JL_SD0/SD1（0x1E1400 / 0x1E1500）。", body)


# ============================================================ sfc
def build_sfc():
    s = Svg(900, 360)
    s.box(30, 40, 190, 80, ["CPU 取指 / 数据", "XIP 零等待读"], "cpu", fs=12.5)
    s.box(30, 200, 190, 80, ["SFC 命令", "CODE：SPI 命令字", "QUCNT：四线计数"], "peri", fs=12.5)
    s.box(330, 100, 210, 120, ["SFC 控制器", "CON 控制", "BAUD 分频", "BASE_ADR 映射", "1/2/4 线"], "bus", fs=13)
    s.box(650, 100, 200, 120, ["片内 NOR 1MB", "0x000000-0x0FFFFF", "uboot / app / 资源 / VM", "eFuse 密钥加密"], "mem", fs=12.5)
    s.box(650, 260, 200, 70, ["SFCENC 加密单元", "KEY / 明文区地址"], "ana", fs=12)
    s.arrow(220, 70, 330, 120)
    s.arrow(220, 240, 330, 200)
    s.arrow(540, 160, 650, 160)
    s.line(750, 220, 750, 260)
    s.arrow(650, 295, 540, 180, dashed=True)

    body = ""
    body += sec("ov", "概述", f"""
<p>SFC（Serial Flash Controller）管理 <b>片内 1MB NOR Flash</b>，CPU 经 XIP
直接执行代码（所以代码区只读，运行数据在 RAM/VM）。支持 1/2/4 线命令、
可配四线最高频率（<code>clock_set_sfc_max_freq()</code>），并有 <b>SFCENC</b>
加密单元：按 eFuse ChipKey 对指定地址区加解密——出厂镜像若密钥不符则无法启动。</p>
{fig(s.render(), "图 14-1  SFC 与片内 Flash")}
""")
    body += sec("regs", "寄存器", regtable([
        (0x00, "SFC CON", "RW", "控制器状态/模式（JL_SFC @ 0x1F0200）"),
        (0x04, "SFC BAUD", "WO", "SCK 分频"),
        (0x08, "SFC CODE", "WO", "SPI 命令（读/写/擦除指令字）"),
        (0x0C, "SFC BASE_ADR", "WO", "XIP 映射基地址"),
        (0x10, "SFC QUCNT", "WO", "四线模式计数"),
        (0x00, "SFCENC CON", "RW", "加密使能/模式（JL_SFCENC @ 0x1F0300）"),
        (0x04, "SFCENC KEY", "RW", "密钥（与 eFuse 派生）"),
        (0x08, "SFCENC UNENC_ADRH/L", "WO", "明文区地址窗口"),
        (0x10, "SFCENC LENC_ADRH/L", "WO", "加密区地址窗口"),
    ]))

    body += sec("map", "Flash 内容布局（isd 打包产物）", table(["区域", "内容", "说明"], [
        ("0x0000-", "ISD 头 + 配置", "isd_config.ini 生成的启动参数"),
        ("", "uboot.boot", "二级引导（负责解密/跳转）"),
        ("", "app.bin", "应用程序（XIP 执行）"),
        ("", "资源（音效/字体/配置）", "资源区"),
        ("0xE8000-0xFC000", "VM 区", "syscfg_read/write 的持久存储（80KB）"),
        ("0xFD000-0xFF000", "BTIF / LRIF 保留", "蓝牙状态，升级时保留"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/spiflash.h"
#include "asm/sfc_norflash_api.h"

/* SDK 已把片内 flash 封装为设备，读写以"页/扇区"为单位 */
spi_flash_dev_read(addr, buf, len);       /* 读 */
spi_flash_dev_erase_sector(sector);       /* 擦 4KB（写入前必须擦） */
spi_flash_dev_write(addr, buf, len);      /* 写 */

/* 频率上限（四线 100MHz 需 flash 支持） */
clock_set_sfc_max_freq(40000000, 80000000);

/* VM 持久存储（掉电保存） */
#include "syscfg_id.h"
syscfg_write(CFG_EBOOK_SHELF, &data, sizeof(data));
syscfg_read(CFG_EBOOK_SHELF, &data, sizeof(data));'''))
    body += '<div class="warn">代码区 XIP 只读；任何扇区擦写期间会挂起总线（卡顿数十 ms），实时性敏感路径（音频 ISR）避免触发 flash 写。VM 写入同样如此——阅读器每次翻页写一次 VM 是可接受的频率。</div>\n'
    body += '<div class="note">加密启动：镜像由 isd_download 以 <code>-key</code> 密钥流加密，芯片 eFuse 校验 ChipKey（本机 0xB165）。经实测，离线打包（Device offline）阶段产物与密钥文件无关，烧录失败应先排查 uboot/电源而非盲目换 key。</div>\n'

    page("sfc.html", "CHAPTER 14", "SPI Flash 控制器",
         "片内 1MB NOR 的 XIP 控制器：1/2/4 线命令、SFCENC 加密与 eFuse 校验、VM 持久区——对应 JL_SFC（0x1F0200）/ JL_SFCENC（0x1F0300）。", body)
