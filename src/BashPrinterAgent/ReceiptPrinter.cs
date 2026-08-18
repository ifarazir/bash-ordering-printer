using System.Drawing;
using System.Drawing.Printing;

namespace BashPrinterAgent;

/// <summary>
/// Sends a rendered receipt to a Windows printer as a bitmap.
///
/// Nothing here knows what a receipt says. Thermal units have no Persian glyphs and no
/// right-to-left shaping, so the server renders the whole thing to a PNG and this program
/// only decides how large it lands on the paper.
/// </summary>
public static class ReceiptPrinter
{
    public static IReadOnlyList<string> InstalledPrinters()
    {
        var names = new List<string>();

        foreach (string? name in PrinterSettings.InstalledPrinters)
        {
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Print one copy.
    /// </summary>
    /// <param name="printerName">Windows' own name for the device.</param>
    /// <param name="png">The receipt as PNG bytes.</param>
    /// <param name="dpi">Print head resolution — 203 on the common 80mm units.</param>
    public static void Print(string printerName, byte[] png, int dpi)
    {
        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);

        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = printerName;

        if (!document.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException(
                $"Windows has no printer named \"{printerName}\". Installed: " +
                string.Join(", ", InstalledPrinters()));
        }

        // Page units for a printer Graphics are hundredths of an inch, so the image's pixel
        // count becomes a physical size only once it is divided by the head's dpi. Getting
        // this wrong is what prints a receipt at half width or spills it off the roll.
        var widthHundredths = (int)Math.Round(image.Width * 100.0 / dpi);
        var heightHundredths = (int)Math.Round(image.Height * 100.0 / dpi);

        // A roll has no page to fill, so the "page" is exactly the receipt. Left as the
        // driver's default it feeds a full letter page after every ticket.
        document.DefaultPageSettings.PaperSize = new PaperSize("Receipt", widthHundredths, heightHundredths);
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.OriginAtMargins = false;
        document.DocumentName = "Bash receipt";

        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null)
                return;

            e.Graphics.DrawImage(image, new Rectangle(0, 0, widthHundredths, heightHundredths));
            e.HasMorePages = false;
        };

        document.Print();
    }
}
