using PDFMaster.Models;



namespace PDFMaster.Services;



public static class TextBlockBoundsHelper

{

    public static (double X, double Y, double Width, double Height) GetCoverBounds(PdfTextBlockItem block)

    {

        var x = block.IsMoved || block.IsResized ? block.OriginalX : block.X;

        var y = block.IsMoved || block.IsResized ? block.OriginalY : block.Y;

        var width = block.IsMoved || block.IsResized ? block.OriginalWidth : block.Width;

        var height = block.IsMoved || block.IsResized ? block.OriginalHeight : block.Height;

        return (x, y, width, height);

    }

}

