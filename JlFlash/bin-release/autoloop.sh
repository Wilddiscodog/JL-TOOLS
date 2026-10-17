#!/bin/bash
# Sustained payload runner: keeps re-jumping the payload every time the
# watchdog resets the chip back into download mode.
C=./JlFlash.exe
jumptoloader() {
  # enter the wl82loader (already in RAM after reset) and wait for it
  "$C" scan >/dev/null 2>&1
}
while true; do
  found=$("$C" scan 2>/dev/null | grep -ci uboot)
  if [ "$found" != "0" ]; then
    "$C" loadrun payload.bin 1C40000 2>&1 | grep -E "readback|jump|payload"
  fi
  sleep 2
done
