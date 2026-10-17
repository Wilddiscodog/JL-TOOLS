# -*- coding: utf-8 -*-
"""pages_periph1.py — UART / SPI / IIC / TIMER / MCPWM（f-string 体内只放原生 HTML）"""

from gen_core import (page, sec, p, ul, table, rw, regtable, bits, code,
                      callout, grid, kpis, fig, Svg, esc)


# ============================================================ uart
def build_uart():
    s = Svg(900, 330)
    s.box(30, 40, 170, 60, ["时钟源", "CLK_CON1[11:10]", "默认 PLL48M"], "ana", fs=12)
    s.box(30, 170, 170, 60, ["波特率发生器", "BAUD（写_only）"], "peri", fs=12)
    s.arrow(115, 100, 115, 170)
    s.box(300, 40, 170, 70, ["发送移位器", "TX: TXADR/TXCNT", "DMA 逐包发送"], "peri", fs=12)
    s.box(300, 170, 170, 70, ["接收环形缓冲", "RXSADR/RXEADR/RXCNT", "硬件计数 HRXCNT"], "peri", fs=12)
    s.arrow(200, 70, 300, 70)
    s.arrow(200, 195, 300, 200)
    s.box(600, 100, 190, 70, ["TX/RX 引脚", "IO 复用 CH0-CH2", "（OTCNT 输出触发）"], "dark", fs=12)
    s.arrow(470, 75, 600, 125)
    s.arrow(600, 155, 470, 200)
    s.box(600, 230, 190, 60, ["IRQ_UART0-2", "18 / 19 / 20"], "bus", fs=12)
    s.arrow(560, 240, 600, 255)

    body = ""
    body += sec("ov", "概述", f"""
<p>AC6956C 内置 <b>3 路异步串口</b>（UART0/1/2，寄存器结构相同），特点是接收方向为
<b>硬件环形 DMA 缓冲</b>：CPU 只需查询 <code>HRXCNT</code> 硬件接收计数即可无丢包地
收取流式数据，非常适合调试控制台与外接模块通信。发送方向支持单字节（BUF）与
DMA 描述符（TXADR/TXCNT）两种方式。</p>
{kpis([("3 路", "UART0 / 1 / 2"), ("PLL48M", "默认时钟源（主频无关）"),
       ("环形 RX", "硬件 DMA 接收缓冲"), ("18-20", "中断号")])}
{fig(s.render(), "图 5-1  UART 结构框图")}
""")
    body += sec("regs", "寄存器（JL_UART0 @ 0x1E2000，UART1/2 = +0x100 / +0x200）", regtable([
        (0x00, "CON0", "RW", "控制 0（使能/模式，SDK 驱动管理）"),
        (0x04, "CON1", "RW", "控制 1（红外/IR 模式等）"),
        (0x08, "BAUD", "WO", "波特率分频（驱动按时钟源自动计算）"),
        (0x0C, "BUF", "RW", "发送/接收数据字节（轮询模式）"),
        (0x10, "OTCNT", "RW", "输出触发计数"),
        (0x14, "TXADR", "RW", "发送 DMA 缓冲地址"),
        (0x18, "TXCNT", "WO", "发送字节数（写入即启动 DMA 发送）"),
        (0x1C, "RXSADR", "RW", "接收环形缓冲起始地址"),
        (0x20, "RXEADR", "RW", "接收环形缓冲结束地址"),
        (0x24, "RXCNT", "RW", "接收控制"),
        (0x28, "HRXCNT", "RO", "硬件接收计数（读取后自动清零）"),
    ]))
    body += '<div class="note">SDK 未公开 CON0/CON1 的逐位定义（在闭源驱动库内），应用层应通过 <code>uart_init()</code> / 设备层 API 配置，不要直接写控制位。</div>\n'

    body += sec("use", "使用方法", code(
'''#include "asm/uart.h"

/* 平台数据（board 板级文件中） */
UART0_PLATFORM_DATA_BEGIN(uart0_data);
    .baudrate = 1000000,            /* 调试串口 1M */
    .tx_pin   = IO_PORT_DP,         /* 本机: DP/DM 复用作 TX/RX */
    .rx_pin   = IO_PORT_DM,
    .clk_src  = UART_CLK_SRC_PLL48M,
UART0_PLATFORM_END();

uart_init(&uart0_data);

/* 常用接口（include_lib/driver/device/uart.h） */
uart_send_data(&uart0_dev, buf, len);
uart_recv_data(&uart0_dev, buf, len);   /* 从环形缓冲取走数据 */

/* 典型调试口: printf 走 UART0 */'''))
    body += '<div class="tip">本机实践：充电头供电场景下 USB 会抢占 DP/DM，用「按住任意键上电」进入日志模式可禁用 OTG、把 DP/DM 让给 UART0 做全程日志。</div>\n'

    page("uart.html", "CHAPTER 5", "UART 串口",
         "3 路异步串口：硬件环形 DMA 接收、DMA 发送、红外模式——对应 JL_UART0-2（0x1E2000 起）。", body)


# ============================================================ spi
def build_spi():
    s = Svg(900, 300)
    s.box(30, 30, 180, 56, ["时钟 LSB / 分频", "BAUD"], "ana", fs=12)
    s.box(30, 140, 180, 76, ["线模式", "1bit 全双工 / 1bit 半双工", "2bit / 4bit（仅 SPI0）"], "peri", fs=12)
    s.box(300, 60, 190, 90, ["SPI 核心", "CON 控制", "BUF 单字节", "轮询 PND / 中断"], "cpu", fs=12.5)
    s.box(300, 190, 190, 70, ["DMA", "ADR + CNT", "连续传输"], "peri", fs=12)
    s.arrow(120, 86, 120, 140)
    s.arrow(210, 60, 300, 90)
    s.arrow(210, 178, 300, 200)
    s.arrow(395, 150, 395, 190)
    s.box(620, 100, 200, 90, ["引脚组", "CS / CLK / DI / DO", "（+ D2 / D3 四线）", "每组引脚可映射"], "dark", fs=12)
    s.arrow(490, 105, 620, 130)
    s.arrow(490, 225, 620, 165)

    body = ""
    body += sec("ov", "概述", f"""
<p>内置 <b>3 组 SPI</b>（SPI0/1/2，寄存器相同）。SPI0 额外支持 2/4 位半双工模式
（配合 SFC 可外接四线 NOR）。CS/CLK/DI/DO 引脚有 4 组备选映射，经
<code>spi_platform_data</code> 的 port 字段选择。本机 SPI2 复用作 ST7305 屏幕与
W25Q128 外挂 Flash 总线。</p>
{kpis([("3 组", "SPI0 / 1 / 2"), ("4 线", "SPI0 四位模式"),
       ("4 组", "引脚映射/组"), ("IRQ 14/15/44", "SPI0/1/2 中断")])}
{fig(s.render(), "图 6-1  SPI 框图")}
""")
    body += sec("regs", "寄存器（JL_SPI0 @ 0x1E1C00，SPI1/2 = +0x100 / +0x200）", regtable([
        (0x00, "CON", "RW", "使能/主从/模式/中断/pending（驱动管理）"),
        (0x04, "BAUD", "WO", "波特率分频，<code>spi_set_baud()</code> 计算"),
        (0x08, "BUF", "RW", "单字节收发数据"),
        (0x0C, "ADR", "WO", "DMA 缓冲地址"),
        (0x10, "CNT", "WO", "DMA 传输长度（写入即启动）"),
    ]))
    body += '<div class="note">控制位细节封装在闭源驱动里；应用层走 <code>spi.h</code> 的完整 API（开盒即用，含 ISR 版本）。</div>\n'

    body += sec("modes", "线模式", table(["枚举", "含义", "适用"], [
        ("<code>SPI_MODE_BIDIR_1BIT</code>", "全双工，DI 收 / DO 发", "常规外设"),
        ("<code>SPI_MODE_UNIDIR_1BIT</code>", "半双工，DO 分时收发", "屏/Flash"),
        ("<code>SPI_MODE_UNIDIR_2BIT</code>", "DI+DO 双位分时", "仅 SPI0"),
        ("<code>SPI_MODE_UNIDIR_4BIT</code>", "DI+DO+D2+D3 四位分时", "仅 SPI0"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/spi.h"

/* 板级：引脚组与速率（例：SPI2 用于屏 + 外挂 Flash） */
const struct spi_platform_data spi2_p_data = {
    .port = 'A',                    /* 引脚组选择 */
    .mode = SPI_MODE_BIDIR_1BIT,
    .role = SPI_ROLE_MASTER,
    .clk  = 24000000,
};

spi_open(SPI2);                     /* 打开 */
spi_set_baud(SPI2, 12000000);       /* 运行中改速率 */

/* 轮询单字节 */
spi_send_byte(SPI2, 0x9F);
u8 id = spi_recv_byte(SPI2, &err);

/* DMA 批量（屏刷场景） */
spi_dma_send(SPI2, framebuffer, len);

/* ISR 内使用专用接口（不可睡眠） */
spi_send_byte_for_isr(SPI2, b);
spi_dma_set_addr_for_isr(SPI2, buf, len, 1);'''))
    body += '<div class="warn">多设备共享同一 SPI 总线时（如本机屏 + W25Q128 共用 SPI2），各设备 CS 独立控制，传输前务必确认总线速率兼容；<code>spi_open</code> 会重配引脚与波特率。</div>\n'

    page("spi.html", "CHAPTER 6", "SPI 总线",
         "3 组 SPI 主/从控制器：单字节轮询、DMA 批量传输、1/2/4 线模式——对应 JL_SPI0-2（0x1E1C00 起）。", body)


# ============================================================ iic
def build_iic():
    s = Svg(860, 260)
    s.box(30, 30, 180, 70, ["APB / LSB 时钟", "BAUD 分频"], "ana", fs=12)
    s.box(30, 150, 180, 70, ["SCL 产生", "同步拉伸", "（从机 clock stretch）"], "peri", fs=12)
    s.box(320, 60, 210, 130, ["I²C 核心", "CON0：使能/方向/ACK", "BUF：数据",
                              "START/STOP 时序", "PND / END_PND 状态"], "cpu", fs=12.5)
    s.box(640, 90, 180, 70, ["SDA / SCL", "开漏输出", "4 组引脚映射"], "dark", fs=12.5)
    s.arrow(210, 65, 320, 95)
    s.arrow(210, 185, 320, 155)
    s.arrow(530, 125, 640, 125)

    body = ""
    body += sec("ov", "概述", f"""
<p>1 路硬件 I²C（<code>JL_IIC @ 0x1E2400</code>），支持主机/从机、7 位寻址、
时钟拉伸与滤波。<b>iic_hw.h 给出了完整的 CON0/CON1 位操作宏</b>——这是全芯片
为数不多位定义完全公开的外设，可直接寄存器级编程。另有软件 I²C（iic_soft.h）
可用任意 GPIO 模拟。</p>
{fig(s.render(), "图 7-1  I²C 控制器框图")}
""")
    body += sec("regs", "寄存器（JL_IIC @ 0x1E2400）", regtable([
        (0x00, "CON0", "RW", "主控寄存器（位定义见下）"),
        (0x04, "BUF", "RW", "数据寄存器"),
        (0x08, "BAUD", "WO", "波特率（SCL 频率分频）"),
        (0x0C, "CON1", "RW", "辅助控制（SI 模式/启动 pending）"),
    ]))

    body += sec("con0", "CON0 位定义（来自 iic_hw.h 宏）", bits([
        ("[0]", "enable", "模块使能"),
        ("[1]", "role", "0=主机，1=从机"),
        ("[2]", "cfg_done", "配置生效触发"),
        ("[3]", "dir", "0=输出，1=接收"),
        ("[4]", "preset_end", "预置 STOP"),
        ("[5]", "preset_restart", "预置重复 START"),
        ("[6]", "recv_ack", "接收时应答：0=ACK，1=NACK"),
        ("[7]", "send_is_ack", "（读）发送后收到 ACK 状态"),
        ("[8]", "ie", "字节中断使能"),
        ("[9]", "isel", "0=直通，1=滤波"),
        ("[10]", "end_ie", "传输结束中断使能"),
        ("[12]", "end_pnd_clr", "（写 1）清除结束 pending"),
        ("[13]", "end_pnd", "（读）传输结束标志"),
        ("[14]", "pnd_clr", "（写 1）清除字节 pending"),
        ("[15]", "pnd", "（读）字节传输完成标志"),
    ]))
    body += bits([
        ("CON1[13]", "si_mode_en", "从机 SI 模式"),
        ("CON1[15]", "start_pnd", "（读）检测到 START"),
        ("CON1[14]", "start_pnd_clr", "（写 1）清除 START pending"),
    ])

    body += sec("use", "使用方法", code(
'''#include "asm/iic_hw.h"

/* 寄存器级（宏齐全，主机写示例） */
iic_disable(JL_IIC);
iic_role_host(JL_IIC);
iic_baud_reg(JL_IIC) = 200;         /* SCL 频率分频 */
iic_cfg_done(JL_IIC);
iic_enable(JL_IIC);

/* 发送器件地址+写 */
iic_dir_out(JL_IIC);
iic_buf_reg(JL_IIC) = dev_addr << 1;
while (!iic_pnd(JL_IIC));
iic_pnd_clr(JL_IIC);
if (!iic_send_is_ack(JL_IIC)) { /* NACK 处理 */ }

/* 推荐：SDK 设备层（软件 IIC 同接口，引脚自由） */'''))
    body += '<div class="tip">触摸屏 CST836U、各类传感器用软 I²C 更常见（引脚自由）；硬件 I²C 适合固定引脚的高速场景。</div>\n'

    page("iic.html", "CHAPTER 7", "IIC 总线",
         "硬件 I²C 主/从控制器：完整公开的位定义、START/STOP 硬件时序、滤波与从机时钟拉伸——对应 JL_IIC（0x1E2400）。", body)


# ============================================================ timer
def build_timer():
    s = Svg(860, 280)
    s.box(30, 90, 170, 60, ["LSB 时钟", "分频（CON）"], "ana", fs=12)
    s.box(300, 70, 190, 100, ["32 位计数器", "CNT：当前值", "PRD：周期"], "cpu", fs=13)
    s.arrow(200, 120, 300, 120, "CLK")
    s.box(600, 40, 190, 66, ["中断", "IRQ_TIME0-3 (4-7)"], "bus", fs=12.5)
    s.box(600, 160, 190, 66, ["PWM 输出", "PWM 寄存器 + IO 通道"], "peri", fs=12.5)
    s.arrow(490, 100, 600, 73, "CNT=PRD")
    s.arrow(490, 145, 600, 190, "比较")
    s.box(300, 210, 190, 56, ["系统 TICK", "IRQ 3（OS 时基）"], "peri", fs=12)
    s.line(395, 170, 395, 210, dashed=True)

    body = ""
    body += sec("ov", "概述", f"""
<p>4 个 32 位通用定时器（TIMER0-3），寄存器组仅 4 个：CON / CNT / PRD / PWM。
SDK 用其中之一维护系统 TICK（<code>timer_get_ms()</code>），
<code>timer_pwm_init()</code> 可把任意 TIMER 变成 PWM 输出通道（经 IO 输出通道映射到引脚）。
高频 PWM 需求应改用 MCPWM 外设（8 通道）。</p>
{fig(s.render(), "图 8-1  定时器结构")}
""")
    body += sec("regs", "寄存器（JL_TIMER0 @ 0x1E0400，TIMER1/2/3 = +0x100 递增）", regtable([
        (0x00, "CON", "RW", "使能/时钟分频/计数模式（SDK 驱动管理）"),
        (0x04, "CNT", "RW", "当前计数值（可读写）"),
        (0x08, "PRD", "RW", "周期终值，CNT 计到 PRD 产生事件"),
        (0x0C, "PWM", "RW", "PWM 比较值/极性"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/timer.h"
#include "asm/mcpwm.h"

/* 1. 系统时基（SDK 已自动启用） */
u32 now = timer_get_ms();
u32 sec = timer_get_sec();
delay_2ms(10);                       /* 阻塞延时（2ms 粒度） */

/* 2. OS 级软定时器（推荐，非阻塞） */
#include "system/timer.h"
sys_timer_add(NULL, my_tick_cb, 20);     /* 20ms 周期回调 */
sys_timeout_add(NULL, once_cb, 500);     /* 单次 */

/* 3. TIMERx 直接作 PWM 输出 */
timer_pwm_init(JL_TIMER0,               /* 定时器 */
               20000,                   /* 20kHz */
               500,                     /* 占空比 0-10000 */
               IO_PORTA_02,             /* 输出引脚 */
               CH0_T0_PWM_OUT);         /* IO 输出通道 */
set_timer_pwm_duty(JL_TIMER0, 2500);'''))
    body += '<div class="note">实践建议：UI 刷新/按键扫描用 <code>sys_timer_add</code>（OS 软定时器），只有精确波形才占硬件 TIMER；软关机前记得 <code>sys_timer_del</code> 清理。</div>\n'

    page("timer.html", "CHAPTER 8", "定时器",
         "4 组 32 位定时器：系统时基、周期中断与 TIMER-PWM 输出——对应 JL_TIMER0-3（0x1E0400 起）。", body)


# ============================================================ mcpwm
def build_mcpwm():
    s = Svg(900, 340)
    s.box(30, 30, 180, 56, ["LSB 时钟", "FPIN_CON 外部触发"], "ana", fs=12)
    s.box(30, 200, 180, 100, ["时基 TIMER0-5", "TMRx_CON/CNT/PR", "边沿/中心对齐"], "cpu", fs=12.5)
    s.arrow(120, 86, 120, 200)
    s.box(330, 190, 220, 120, ["比较通道 CH0-CH7", "CHx_CON0 / CON1", "CMPH / CMPL",
                               "互补死区（complementary）"], "peri", fs=12.5)
    s.arrow(210, 250, 330, 250, "时基")
    s.box(670, 190, 170, 120, ["H / L 引脚", "每通道 2 个输出", "硬件引脚或", "IO 通道重映射"], "dark", fs=12)
    s.arrow(550, 250, 670, 250)

    body = ""
    body += sec("ov", "概述", f"""
<p>MCPWM 是独立的 <b>多通道 PWM 发生器</b>：6 个时基定时器 × 8 个比较通道，
每通道 H/L 两路输出，支持边沿/中心对齐与互补（带死区）模式，
用于背光调光、电机驱动、呼吸灯等。寄存器在 <code>JL_MCPWM @ 0x1E3000</code>，
SDK 提供 <code>struct pwm_platform_data</code> 描述式配置。</p>
{fig(s.render(), "图 9-1  MCPWM 结构（6 时基 × 8 通道）")}
""")
    body += sec("regs", "寄存器布局（0x1E3000，按 32 位访问）", regtable([
        (0x000, "TMR0_CON/CNT/PR", "RW", "时基 0：控制/计数/周期（3×4B）"),
        (0x00C, "TMR1_CON/CNT/PR", "RW", "时基 1"),
        (0x018, "TMR2 - TMR5", "RW", "时基 2-5（各 0x0C）"),
        (0x05C, "FPIN_CON", "RW", "故障/外部引脚控制"),
        (0x060, "CH0_CON0/CON1/CMPH/CMPL", "RW", "通道 0：控制、比较高/低"),
        (0x070, "CH1 - CH7", "RW", "通道 1-7（各 0x10）"),
        (0x0F0, "MCPWM_CON0", "RW", "全局控制"),
    ]))

    body += sec("cfg", "pwm_platform_data 配置项", table(["字段", "说明"], [
        ("<code>frequency</code>", "PWM 频率（该时基统一）"),
        ("<code>pwm_ch_num</code>", "通道 <code>pwm_ch0-ch5</code>"),
        ("<code>pwm_timer_num</code>", "时基 <code>pwm_timer0-5</code>"),
        ("<code>duty</code>", "占空比 0-10000（对应 0-100%）"),
        ("<code>pwm_aligned_mode</code>", "<code>pwm_edge_aligned</code> 边沿 / <code>pwm_center_aligned</code> 中心"),
        ("<code>h_pin / l_pin</code>", "通道高低引脚；-1 表示不接硬件引脚"),
        ("<code>h/l_pin_output_ch_num</code>", "非硬件引脚时占用的 GPIO 输出通道号（0-2）"),
        ("<code>complementary_en</code>", "1=H/L 同相，0=互补（带死区）"),
    ]))

    body += sec("use", "使用方法", code(
'''#include "asm/mcpwm.h"

struct pwm_platform_data pwm0 = {
    .frequency        = 1000,            /* 1kHz */
    .pwm_ch_num       = pwm_ch0,
    .pwm_timer_num    = pwm_timer0,
    .duty             = 5000,            /* 50% */
    .pwm_aligned_mode = pwm_edge_aligned,
    .h_pin            = IO_PORTB_04,     /* 硬件 H 引脚 */
    .l_pin            = -1,
    .complementary_en = 1,
};
/* 设备层注册后由 dev->init 生效（SDK 驱动枚举 MCPWM 设备） */

/* 运行时改占空比：直接写比较寄存器 */
JL_MCPWM->CH0_CMPL = new_duty;'''))
    body += '<div class="note">注意通道枚举 <code>pwm_ch_max = 6</code> 与硬件 8 通道（CH0-7）的差异：SDK 设备层仅暴露 6 通道；CH6/CH7 可经寄存器直接使用。</div>\n'

    page("mcpwm.html", "CHAPTER 9", "MCPWM 多通道 PWM",
         "6 时基 × 8 比较通道的 PWM 发生器：边沿/中心对齐、互补死区、H/L 双输出与通道重映射——对应 JL_MCPWM（0x1E3000）。", body)
