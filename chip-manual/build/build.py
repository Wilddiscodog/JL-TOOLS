# -*- coding: utf-8 -*-
"""build.py — 生成全部手册页面"""
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import pages_core
import pages_periph1
import pages_periph2
import pages_sys

pages_core.build_index()
pages_core.build_clock()
pages_core.build_gpio()
pages_core.build_irq()

pages_periph1.build_uart()
pages_periph1.build_spi()
pages_periph1.build_iic()
pages_periph1.build_timer()
pages_periph1.build_mcpwm()

pages_periph2.build_adc()
pages_periph2.build_audio()
pages_periph2.build_usb()
pages_periph2.build_sdio()
pages_periph2.build_sfc()

pages_sys.build_power()
pages_sys.build_dmasec()
pages_sys.build_misc()

print("\nDone ->", os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
