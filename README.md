# PDFMaster

Modern Windows PDF editor, compressor, merge, and tools suite built with **WPF** and **.NET 8**.

## Features

| Tab | Purpose |
|-----|---------|
| **Edit PDF** | Pages (drag to reorder), text, images, signatures, stamps, shapes, highlights, whiteout, form fields, undo/redo, save with compress |
| **Compress** | Single PDF or batch folder; Ghostscript for High/Maximum levels |
| **Merge PDF** | Combine multiple PDFs in custom order |
| **Tools** | Split PDF, extract page ranges, text watermark |

## Languages

English, Arabic (RTL), Spanish, French, German, Chinese, Swedish, Norwegian.

## Quick start

```bat
build.bat           REM build Release x64
start.bat           REM build + run
setup-tools.bat     REM optional Ghostscript for max compression
publish-standalone.bat
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows x64.

## Optional: Ghostscript

For strongest compression (High / Maximum), install Ghostscript or copy `gswin64c.exe` to `PDFMaster\tools\`. Run `setup-tools.bat` for instructions. The app also auto-detects Ghostscript from `Program Files`.

## Project layout

See [docs/STRUCTURE.md](docs/STRUCTURE.md).

## Author

Copyright © 2026 mothannakh · [https://satisfy.live/](https://satisfy.live/)
