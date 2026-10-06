# Project Structure

Complete layout and architecture reference for **PDFMaster**.

---

## Repository tree

```
PDFMaster/
│
├── PDFMaster.sln               # Visual Studio solution (Release | x64)
├── Directory.Build.props       # Shared MSBuild: Release, x64, win-x64 RID
│
├── LICENSE                     # MIT
├── README.md
├── .gitignore
│
├── docs/
│   ├── STRUCTURE.md            # This file
│   └── TODO.md                 # Phase roadmap
│
├── start.bat                   # Build Release x64 + run app
├── build.bat                   # Build Release x64 only
├── publish-standalone.bat      # Self-contained publish → publish/
├── release-github.bat          # Publish, GitHub release upload, cleanup publish/ + zip
├── setup-tools.bat             # Ghostscript setup instructions
│
└── PDFMaster/                  # Main WPF project (.NET 8)
    ├── PDFMaster.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── SettingsWindow.xaml / SettingsWindow.xaml.cs
    ├── AboutWindow.xaml / AboutWindow.xaml.cs
    │
    ├── Models/
    │   ├── AppSettings.cs
    │   ├── Enums.cs
    │   ├── PdfPageItem.cs
    │   └── PdfMergeItem.cs
    │
    ├── Services/
    │   ├── PdfDocumentService.cs   # Edit: text, images, pages
    │   ├── PdfRenderService.cs     # Page preview (Docnet/PDFium)
    │   ├── PdfCompressService.cs   # Stream compression
    │   ├── PdfMergeService.cs      # Ordered merge
    │   ├── SettingsService.cs
    │   ├── ThemeService.cs
    │   ├── LocalizationService.cs
    │   ├── FormatHelpers.cs
    │   └── InputPrompt.cs
    │
    ├── Views/
    │   ├── PdfEditorView.xaml
    │   ├── PdfCompressView.xaml
    │   ├── PdfMergeView.xaml
    │   └── PdfToolsView.xaml     # Split, extract, watermark
    │
    ├── Styles/
    │   └── Controls.xaml
    │
    └── Themes/
        ├── DarkTheme.xaml
        └── LightTheme.xaml
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      MainWindow (WPF UI)                     │
│  Edit PDF · Compress · Merge · Settings · Theme · Language   │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
        ┌───────▼────────┐            ┌───────▼────────┐
        │ PdfEditorView  │            │ PdfMergeView   │
        │ PdfCompressView│            │                │
        └───────┬────────┘            └───────┬────────┘
                │                             │
    ┌───────────▼───────────┐       ┌─────────▼─────────┐
    │ PdfDocumentService    │       │ PdfMergeService   │
    │ PdfRenderService      │       │ PdfCompressService│
    │ (PDFsharp + Docnet)   │       │ (PDFsharp)        │
    └───────────────────────┘       └───────────────────┘
```

---

## Settings persistence

| Path | Purpose |
|------|---------|
| `%AppData%\PDFMaster\settings.json` | Theme, language, save path, compress-on-save |

---

## Scripts reference

| Script | Steps |
|--------|-------|
| `start.bat` | Clean Debug → `dotnet build -c Release` → launch exe |
| `build.bat` | Clean Debug → `dotnet build -c Release` |
| `publish-standalone.bat` | Clean → `dotnet publish` single-file → `publish/` |

---

## NuGet dependencies

| Package | Role |
|---------|------|
| **PDFsharp** 6.x | PDF read/write, merge, annotations, compression options |
| **Docnet.Core** | PDFium-based page rendering for previews |
