using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UglyToad.PdfPig;

/// <summary>
/// Bonus: PDF text per page via UglyToad.PdfPig (all DLLs under Assets/Plugins/PdfPig).
/// Usage: copy your .pdf into Assets/StreamingAssets/, match the filename on BookController (Pdf File In Streaming Assets),
/// enable Load Pdf On Start — or set Pdf Absolute Path and call BookController.ReloadPagesFromPdf(). Prefer text-based PDFs.
/// </summary>
public static class BookPdfImporter
{
    public static string StreamingAssetsPath(string fileName)
    {
        return Path.Combine(Application.streamingAssetsPath, fileName);
    }

    /// <summary>
    /// Reads each PDF page as one string (paragraphs may be flattened). Returns false on error.
    /// </summary>
    public static bool TryLoadPages(string absolutePath, out string[] pages, out string errorMessage)
    {
        pages = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            errorMessage = "PDF path is empty.";
            return false;
        }

        if (!File.Exists(absolutePath))
        {
            errorMessage = "PDF file not found: " + absolutePath;
            return false;
        }

        try
        {
            using (PdfDocument document = PdfDocument.Open(absolutePath))
            {
                var list = new List<string>();
                foreach (var page in document.GetPages())
                {
                    string t = page.Text;
                    if (string.IsNullOrWhiteSpace(t))
                        t = " ";
                    list.Add(t.Trim());
                }

                if (list.Count == 0)
                {
                    errorMessage = "PDF contains no pages.";
                    return false;
                }

                pages = list.ToArray();
                return true;
            }
        }
        catch (System.Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
