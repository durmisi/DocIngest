using System;
using System.IO;
using System.Threading.Tasks;
using Xceed.Words.NET;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace DocIngest.Core.Services;

/// <summary>
/// Default implementation of IDocumentGenerator that supports Word and PDF formats.
/// </summary>
public class DefaultDocumentGenerator : IDocumentGenerator
{
    public async Task<string> GenerateDocumentAsync(string text, string documentName, string format, string outputDir)
    {
        var extension = format.Equals("Word", StringComparison.OrdinalIgnoreCase) ? "docx" : format.ToLower();
        var fileName = $"{documentName}.{extension}";
        var outputPath = Path.Combine(outputDir, fileName);

        if (format.Equals("Word", StringComparison.OrdinalIgnoreCase))
        {
            using (var doc = DocX.Create(outputPath))
            {
                var paragraphs = text.Split('\n');
                foreach (var paragraph in paragraphs)
                {
                    doc.InsertParagraph(paragraph);
                }
                doc.Save();
            }
        }
        else if (format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pdf = new PdfDocument();
                var page = pdf.AddPage();
                var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Times-Roman", 12);
                gfx.DrawString(text, font, XBrushes.Black, new XRect(10, 10, page.Width - 20, page.Height - 20), XStringFormats.TopLeft);
                pdf.Save(outputPath);
            }
            catch (InvalidOperationException)
            {
                // Fallback to text file if font not available
                outputPath = Path.ChangeExtension(outputPath, ".txt");
                await File.WriteAllTextAsync(outputPath, text);
            }
        }
        else
        {
            throw new NotSupportedException($"Output format {format} not supported");
        }

        return outputPath;
    }
}