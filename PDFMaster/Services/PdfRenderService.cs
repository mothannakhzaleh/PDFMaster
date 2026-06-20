using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace PDFMaster.Services;

public sealed class PdfRenderService
{
    public ImageSource? RenderPage(string filePath, int pageIndex, int maxWidth = 960)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(maxWidth, maxWidth * 2));
            if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                return null;

            using var pageReader = docReader.GetPageReader(pageIndex);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            return CreateBitmap(rawBytes, width, height);
        }
        catch
        {
            return null;
        }
    }

    public int GetPageCount(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(256, 256));
            return docReader.GetPageCount();
        }
        catch
        {
            return 0;
        }
    }

    public (byte[] Bytes, int Width, int Height)? GetPagePixels(string filePath, int pageIndex, int maxWidth = 960)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(maxWidth, maxWidth * 2));
            if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                return null;

            using var pageReader = docReader.GetPageReader(pageIndex);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var rawBytes = PdfRenderService.CompositeOnWhite(pageReader.GetImage(), width, height);
            return (rawBytes, width, height);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource CreateBitmap(byte[] rawBytes, int width, int height)
    {
        var composite = CompositeOnWhite(rawBytes, width, height);
        var stride = width * 4;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, composite, stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] CompositeOnWhite(byte[] rawBytes, int width, int height)
    {
        var pixelCount = width * height;
        if (rawBytes.Length < pixelCount * 4)
            return rawBytes;

        var composite = new byte[rawBytes.Length];
        for (var i = 0; i < pixelCount; i++)
        {
            var index = i * 4;
            var alpha = rawBytes[index + 3] / 255.0;
            if (alpha >= 0.999)
            {
                composite[index] = rawBytes[index];
                composite[index + 1] = rawBytes[index + 1];
                composite[index + 2] = rawBytes[index + 2];
            }
            else
            {
                composite[index] = (byte)(rawBytes[index] * alpha + 255 * (1 - alpha));
                composite[index + 1] = (byte)(rawBytes[index + 1] * alpha + 255 * (1 - alpha));
                composite[index + 2] = (byte)(rawBytes[index + 2] * alpha + 255 * (1 - alpha));
            }

            composite[index + 3] = 255;
        }

        return composite;
    }
}
