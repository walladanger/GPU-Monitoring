# Live NVIDIA GPU Dashboard

A local Windows dashboard that charts NVIDIA GPU utilization, VRAM usage, core temperature, and power draw. Multiple GPUs appear separately, identified by their GPU UUID.

## Requirements

- Windows with Python 3.10 or newer
- NVIDIA drivers providing `nvidia-smi` on PATH
- A web browser

No third-party Python packages or external web assets are required.

## Run

Double-click **Start GPU Dashboard.cmd**, or run:

```powershell
python gpu_dashboard.py
```

Open http://127.0.0.1:8765/. The script also opens the address in your default browser. Keep the terminal open; press Ctrl+C to stop the server.

Readings refresh approximately every two seconds. Select a 5-, 15-, or 30-minute chart window. Up to 900 samples are retained in memory; history resets when the server stops. The server listens only on the local loopback interface.

## Limitations

- Temperature is GPU core temperature, not hotspot or memory junction temperature.
- Unsupported readings appear as `N/A`.
- Only one dashboard server can use port 8765 at a time.
- This version uses Windows process flags and is intended for Windows.
- Monitoring is read-only: no fan, clock, or power settings are changed.
