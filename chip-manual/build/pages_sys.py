# -*- coding: utf-8 -*-
"""pages_sys.py — 电源管理 / DMA 与安全加速器 / 其他外设（f-string 体内只放原生 HTML）"""

from gen_core import (page, sec, p, ul, table, rw, regtable, bits, code,
                      callout, grid, kpis, fig, Svg, esc)


# ============================================================ power
def build_power():
    s = Svg(900, 400)
    s.box(30, 30, 190, 90, ["VBAT 3.0-4.2V", "（锂电池）", "AD_CH_VBAT ÷4 监测"], "mem", fs=12.5)
    s.box(300, 30, 190, 90, ["内部 DCDC / LDO", "VDC13 / SYSVDD", "VDDIOM 3.4V", "VDDIOW 3.2V"], "cpu", fs=12.5)
    s.box(570, 30, 180, 90, ["LDO5V", "USB/充电输入", "AD_CH_LDO5V ÷4"], "ana", fs=12.5)
    s.box(300, 170, 190, 90, ["P33 掉电域", "RTC / LRC 32K", "唤醒检测 / 充电", "（软关机后仅此域活）"], "peri", fs=12)
    s.box(570, 170, 180, 90, ["LVD 监测", "P3_VLVD_CON", "VLVD=4 → 2.3V 复位"], "ana", fs=12)
    s.box(30, 300, 400, 70, ["应用处理器（正常域）：pi32v2 + 外设", "power_set_soft_poweroff() 掉电式关机"], "bus", fs=12.5)
    s.arrow(125, 120, 125, 300)
    s.arrow(220, 75, 300, 75)
    s.arrow(490, 75, 570, 75)
    s.arrow(395, 120, 395, 170)
    s.arrow(490, 195, 570, 195)
    s.arrow(430, 260, 230, 300, "唤醒", dashed=True)

    body = ""
    body += sec("domains", "电源域", f"""
<p>芯片分两个电源域：<b>正常域</b>（CPU/外设，由 VDDIOM/VDDIOW 供电，可配置
强弱两档：本机 VDDIOM=3.4V、VDDIOW=3.2V，经 <code>P3_ANA_CON5</code>）与
<b>P33 掉电域</b>（RTC、LRC 32K、唤醒检测、充电管理，独立供电，软关机后唯一存活的域）。
两域经 <code>JL_P33</code> 的 SPI 接口通信。</p>
{fig(s.render(), "图 15-1  电源域与监测点")}
""")
    body += sec("regs", "寄存器", regtable([
        (0x00, "P33 PMU_CON", "RW", "PMU 接口控制（JL_P33 @ 0x1E3E00）"),
        (0x04, "P33 RTC_CON", "RW", "RTC 控制"),
        (0x08, "P33 SPI_CON", "RW", "P33 串行接口控制（含片选 BIT0/BIT8）"),
        (0x0C, "P33 SPI_DAT", "RW", "P33 数据读写"),
    ]))
    body += p("P33 域内部寄存器经 SPI 接口按地址访问（<code>p33.h</code>），常用：")
    body += table(["P33 地址", "名称", "说明"], [
        ("<code>0x00-0x09</code>", "P3_ANA_CON0-9", "模拟配置（IO 电压档位 CON5 等）"),
        ("<code>0x0C/0x0D</code>", "P3_L5V_CON0/1", "LDO5V 控制（充电输入）"),
        ("<code>0x0E</code>", "P3_SDPG_CON", "SD/TF 供电轨（POWER FET）"),
        ("<code>0x0F</code>", "P3_FSPG_CON", "Flash/屏供电轨（POW_FLS）"),
        ("<code>0x10</code>", "P3_PLVD_CON", "PLL/LDO 欠压检测"),
        ("<code>0x11</code>", "P3_VLVD_CON", "VDDIOW 欠压检测（VLVD 档位：4=2.3V 复位阈值）"),
        ("<code>0x12</code>", "P3_RST_SRC", "（读）复位来源"),
        ("<code>0x13/0x14</code>", "P3_LRC_CON0/1", "LRC 32K 振荡控制"),
        ("<code>0x18/0x19</code>", "P3_CHG_WKUP/READ", "充电唤醒与状态"),
    ])
    body += '<div class="note">P33 访问必须用 <code>p33_cs_h()/p33_cs_l</code> 临界区宏包裹（内部关中断防撕裂），SDK 提供 <code>P33_TX_NBIT / P33_OR / P33_AND / P33_XOR</code> 及 <code>P33_CON_GET</code> 完整原语。</div>\n'

    body += sec("chg", "充电管理（charge.h）", table(["参数", "范围"], [
        ("充满电压", "3.869V - 4.567V（16 档，<code>CHARGE_FULL_V_xxx</code>）"),
        ("充满截止电流", "2mA - 30mA（8 档）"),
        ("恒流充电电流", "20mA - 320mA（<code>CHARGE_mA_xxx</code>）"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/power/p33.h"
#include "asm/power_interface.h"

/* 软关机（掉电式：P33 域保留，等按键/RTC 唤醒 = 完整重启） */
power_set_soft_poweroff();

/* 复位来源（调试上电原因） */
u8 power_reset_source_dump();

/* 低功耗（睡眠）由系统框架按 idle 自动调度 */
struct low_power_param param = { ... };
power_init(&param);

/* 电压监测 */
u8 pct = get_vbat_percent();        /* 电量百分比（状态栏用） */'''))
    body += '<div class="warn">本机实测：<b>软关机再开机 = 完整重启</b>（非休眠恢复）；uboot 的按键采样每次开机都会执行。欠压阈值 VLVD 档位在 isd_config.ini 里配置（VLVD=4 → 2.3V），过低会导致锂电末段频繁复位。</div>\n'

    page("power.html", "CHAPTER 15", "电源管理（PMU / P33）",
         "双电源域架构：VDDIOM/VDDIOW 电压档、P33 掉电域、LVD 复位、充电管理与软关机——对应 JL_P33（0x1E3E00）与 P3_* 寄存器组。", body)


# ============================================================ dma-sec
def build_dmasec():
    body = ""
    body += sec("dma", "系统 DMA（JL_DMA @ 0x1E3F00）", f"""
<p>总线级 DMA 控制器，各外设（UART/SPI/SD/AUDIO…）的 DMA 请求经它仲裁。
多数场景由外设自身的 DMA 描述符完成（如 UART 环形缓冲），此控制器用于
存储器间搬运配置。</p>
{regtable([
    (0x00, "PRI0-3", "RW", "通道优先级"),
    (0x20, "MSG", "RW", "消息/触发"),
    (0x24, "MSG_CH", "RO", "（读）当前通道"),
    (0x28, "RDL / RDH", "RW", "读地址低/高"),
    (0x30, "WRL / WRH", "RW", "写地址低/高"),
])}
""")
    body += sec("crc", "CRC16 加速（JL_CRC @ 0x1E3500）", f"""
<p>硬件 CRC16 计算：向 FIFO 连续写入数据，结果存于 REG。SDK 封装为
<code>CRC16(ptr, len)</code>，用于镜像校验（打包产物头部 CRC）与通信校验。</p>
{regtable([
    (0x00, "FIFO", "WO", "数据入口（写即计算）"),
    (0x04, "REG", "RW", "CRC 结果 / 初值"),
])}
{code('''#include "asm/crc16.h"
u16 c = CRC16(buf, len);                 /* 一步计算 */
u16 c2 = CRC16_with_initval(buf, len, 0xFFFF);
spi_crc16_set(init);                     /* 手动预置（连续分段计算） */
u16 v = spi_crc16_get();''')}
""")
    body += sec("aes", "AES 加速（JL_AES @ 0x1E4300）", f"""
<p>AES 引擎，寄存器含 NONCE/HEADER/TAGLEN/TAGRES——支持 <b>AES-GCM</b>
（带认证标签）及常规模式，源/目的地址为总线 DMA。蓝牙加密与固件校验使用。</p>
{regtable([
    (0x00, "CON", "RW", "模式/启动"),
    (0x04, "DATIN", "RW", "输入数据"),
    (0x08, "KEY", "WO", "密钥"),
    (0x0C, "ENCRES0-3", "RW", "结果寄存器"),
    (0x1C, "NONCE", "WO", "GCM nonce"),
    (0x20, "HEADER", "WO", "GCM AAD"),
    (0x24, "SRCADR / DSTADR", "WO", "DMA 源/目的"),
    (0x2C, "CTCNT", "WO", "计数"),
    (0x30, "TAGLEN", "WO", "标签长度"),
    (0x34, "TAGRES0-3", "RO", "认证标签结果"),
])}
""")
    body += sec("rand", "真随机数 / 外设流加密", ul([
        "<b>JL_RAND @ 0x1E3B00</b>：<code>R64L/R64H</code>（RO）读出 64 位真随机数，蓝牙地址/密钥熵源。",
        "<b>JL_PERIENC @ 0x1E4100</b>：SPI0/SD0 数据流加密（ENCCON/ENCKEY/ENCADR），TF 卡加密读写场景。",
        "<b>JL_DCP @ 0x1F1300</b>（HSB）：数据拷贝加速器（CON/ADR，IRQ 48），大块 memcpy 卸载。",
    ]))
    body += '<div class="tip">这些加速器 SDK 都有高层封装（crc16.h / crypto 库），仅在极限优化时才需要寄存器级操作；AES-GCM 的密钥调度请信任 SDK 实现。</div>\n'

    page("dma-sec.html", "CHAPTER 16", "DMA 与安全加速器",
         "系统 DMA、硬件 CRC16、AES-GCM、真随机数与外设流加密——对应 JL_DMA / CRC / AES / RAND / PERIENC / DCP。", body)


# ============================================================ misc
def build_misc():
    body = ""
    body += sec("wdt", "看门狗（RTC 域）", f"""
<p>看门狗位于 RTC/RTC_WDT 中断域（IRQ 10），16 档超时 1ms-32s。SDK 启动即开启，
长循环里手动 <code>wdt_clear()</code>（本机目录扫描等长任务均有演示）。</p>
{code('''#include "asm/wdt.h"
wdt_init(WDT_8S);        /* 或 WDT_1MS ... WDT_32S */
wdt_clear();             /* 喂狗 */
wdt_close();             /* 关闭（调试慎用） */
u32 ms = wdt_get_time(); /* 剩余毫秒 */''')}
""")
    body += sec("cnt", "计数器类外设", table(["外设", "基址", "寄存器", "用途"], [
        ("PCNT 脉冲计数", "<code>0x1E1000</code>", "CON / VAL", "外部脉冲计数"),
        ("GPCNT 全局计数", "<code>0x1E1100</code>", "CON / NUM(RO)", "任意时钟源计数（CLK_CON1[6:4] 选源：OSC/PLL480M/IRFLT…）"),
        ("LRCT 低功耗计数", "<code>0x1E3600</code>", "CON(WO) / NUM(RW)", "RTC 域低功耗定时"),
        ("RDEC0-2 旋转解码", "<code>0x1E2C00/4400/4500</code>", "CON / DAT (u8)", "编码器 AB 相硬解码（音量旋钮）"),
    ]))
    body += sec("pled", "PWM 呼吸灯（JL_PLED @ 0x1E5200）", f"""
<p>硬件呼吸灯控制器：CON0-3 全局配置，BRI_PRDL/H（呼吸周期）、BRI_DUTY0/1
（呼吸占空）、PRD_DIVL + DUTY0-3（普通 PWM），<code>CNT_RD</code>（RO）读计数。
SDK 经 <code>pwm_led.h</code> API 配置。</p>
{regtable([
    (0x00, "CON0-3", "RW", "模式/时钟/极性"),
    (0x10, "BRI_PRDL/H", "RW", "呼吸周期低/高"),
    (0x14, "BRI_DUTY0L/H / DUTY1L/H", "RW", "呼吸占空"),
    (0x28, "PRD_DIVL", "RW", "普通 PWM 周期分频"),
    (0x2C, "DUTY0-3", "RW", "普通 PWM 占空 ×4"),
    (0x3C, "CNT_RD", "RO", "计数器读出"),
])}
""")
    body += sec("others", "其余模块速览", table(["模块", "基址", "寄存器", "说明"], [
        ("MODE", "<code>0x1E0100</code>", "MODE_CON", "芯片工作模式"),
        ("RST 源", "<code>0x1E00C0</code>", "SRC(RO)", "复位来源读出"),
        ("SYSTEM", "<code>0x1E0200</code>", "CHIP_ID / MBIST_CON", "芯片 ID 与 RAM 自检"),
        ("IR 滤波", "<code>0x1E3200</code>", "RFLT_CON", "红外接收滤波"),
        ("OSA", "<code>0x1E3400</code>", "CON", "振荡/系统辅助"),
        ("CTM 触摸", "<code>0x1E3C00</code>", "CON0/CON1/ADR", "内置电容触摸（本机用外挂 CST836U/I²C）"),
        ("PAP 并口", "<code>0x1E2800</code>", "CON/DAT0/1/BUF/ADR/CNT", "8 位并口（摄像头/并口屏）"),
        ("PLNK PDM", "<code>0x1E2D00</code>", "CON/SMR/ADR/LEN", "PDM 麦克风链路"),
        ("SS 同步串口", "<code>0x1E2B00</code>", "CON/SR_CNT/IO_CON/DMA…", "同步串行（含 SPDIF 用途）"),
        ("LCD 偏压", "<code>0x1E5300</code>", "CON0/SEG_IOEN0/1", "段码 LCD 偏压与段 IO 使能"),
        ("EFUSE", "<code>0x1E3700</code>", "CON / ME", "eFuse 读取（ChipKey 烧录校验）"),
    ]))
    body += '<div class="note">以上寄存器布局来自 <code>br23.h</code> 结构体定义（成员即寄存器、每成员 4 字节）；除标注 u8/u16 访问的（RDEC/PERIENC/SD 部分）外均按 32 位访问。具体位定义 SDK 多数未公开——优先使用对应的 API 头文件。</div>\n'

    page("misc.html", "CHAPTER 17", "其他外设",
         "看门狗、脉冲计数、旋转解码、PWM 呼吸灯、并口/PDM/段码 LCD 等其余片上模块速览（基址 + 寄存器布局 + SDK API 指引）。", body)
