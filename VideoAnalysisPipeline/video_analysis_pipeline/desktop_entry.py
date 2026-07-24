"""
Desktop entry point for the Video Analysis Pipeline.

This module bypasses bootstrap (venv creation and pip install) and directly
invokes the CLI main function. It is designed to be called by the WPF desktop
application with a pre-built, self-contained Python runtime::

    <install_dir>\\engine\\python\\python.exe -m video_analysis_pipeline.desktop_entry <args...>

Do NOT call this via ``py run_pipeline.py``, PowerShell, or cmd.exe wrappers.
"""

from __future__ import annotations

import sys

from video_analysis_pipeline.cli import main

if __name__ == "__main__":
    sys.exit(main())
