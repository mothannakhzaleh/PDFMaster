namespace PDFMaster.Models;

public sealed class AppSettings
{
    public string DefaultSavePath { get; set; } = string.Empty;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public bool CompressOnSave { get; set; }
    public PdfCompressLevel DefaultCompressLevel { get; set; } = PdfCompressLevel.Medium;
}
