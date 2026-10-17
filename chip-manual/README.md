# AC6956C（BR23）芯片参考手册

依据本 SDK 头文件逆向整理的 HTML 版芯片参考手册（芯片无官方手册，本手册
寄存器地址/位域均可在 `include_lib/driver/cpu/br23/asm/` 下复核）。

## 阅读

直接双击 `index.html` 即可（纯静态页面，无外部依赖），或：

```
cd docs/chip_manual
python -m http.server 8931
# 浏览器打开 http://127.0.0.1:8931/
```

## 章节目录

| 文件 | 内容 |
|---|---|
| index.html | 芯片总览、总线架构框图、外设地址映射 |
| clock.html | 时钟树、CLK_CON0-3 / SYS_DIV / PLL_CON 位定义 |
| gpio.html | PORTA-D 寄存器、IOMAP 输出通道、ADC 复用、唤醒 |
| irq.html | 64 个中断源总表、request_irq 用法 |
| uart.html | UART0-2 寄存器与环形 DMA 收发 |
| spi.html | SPI0-2、线模式、DMA |
| iic.html | 硬件 I²C（完整位定义） |
| timer.html | TIMER0-3 与 TIMER-PWM |
| mcpwm.html | 8 通道 PWM（6 时基） |
| adc.html | SARADC 16 通道、轮询采样表 |
| audio.html | AUDIO FIFO、ANA 模拟链、ALNK |
| usb.html | USB FS 设备、端点、UTMI 寄存器 |
| sdio.html | SD0/SD1（本机 TF = SD1） |
| sfc.html | 片内 1MB NOR、XIP、SFCENC 加密、VM 区 |
| power.html | P33 掉电域、充电、LVD、软关机 |
| dma-sec.html | DMA、CRC16、AES、随机数 |
| misc.html | WDT/RDEC/PLED/PCNT/GPCNT 等速览 |

## 重新生成

页面由 Python 生成器产出（改内容请编辑生成器后重跑，勿直接改 HTML）：

```
cd docs/chip_manual/build
python build.py
```

- `gen_core.py` —— 页面模板 / 表格组件 / SVG 绘图 DSL（配色、箭头、框图）
- `pages_core.py` —— 总览 / 时钟 / GPIO / 中断
- `pages_periph1.py` —— UART / SPI / IIC / TIMER / MCPWM
- `pages_periph2.py` —— ADC / 音频 / USB / SDIO / SFC
- `pages_sys.py` —— 电源 / DMA 与加密 / 其他外设

注意：f-string 模板体内只放原生 HTML（Python 3.11 不允许 f-string
表达式内出现跨行普通字符串）；代码示例统一用三单引号块。

## 准确性说明

- 寄存器偏移来自 `br23.h` 结构体成员顺序（每成员 4 字节）；
  基址 = 段基址 + (64×grp + adr)×4。
- 位定义仅收录 SDK 头文件明确给出的部分（clock_hw.h / iic_hw.h / usb.h /
  hwi.h / adc_api.h / wdt.h 等）；其余以 SDK API 层面描述为准，
  未公开位域的地方在页面中有标注。
- 手册含本机（词易帮 Y20 / AC6956C8）实测经验提示，换板时注意甄别。
