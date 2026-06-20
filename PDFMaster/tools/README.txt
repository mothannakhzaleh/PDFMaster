PDFMaster bundled tools
=======================

Ghostscript (for High / Maximum PDF compression)
------------------------------------------------

1. Download "Ghostscript for Windows (64 bit)" from:
   https://ghostscript.com/releases/gsdnld.html
   (file name is usually gs10071w64.exe or similar)

2. Copy the installer into THIS folder:
   PDFMaster\tools\

3. Run from the repo root:
   setup-tools.bat

   The script will silently install Ghostscript to tools\gs\
   and PDFMaster will find gswin64c.exe automatically.

Alternative: run the Ghostscript installer normally (double-click),
then run setup-tools.bat again — it copies from Program Files.

You can also install Ghostscript system-wide only; PDFMaster auto-detects
C:\Program Files\gs\gs*\bin\gswin64c.exe without bundling.
