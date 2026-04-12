using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PosReceiptPdfService : IPosReceiptPdfService
{
    public byte[] Generate(Order order, PosPayment? payment)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, order));
                page.Content().Element(c => ComposeContent(c, order, payment));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, Order order)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Speise-Direkt").Bold().FontSize(18).FontColor(Colors.Orange.Medium);
                    left.Item().Text("Beleg / Rechnung").FontSize(12).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(180).AlignRight().Column(right =>
                {
                    right.Item().Text($"Belegnummer: {order.OrderNumber}").Bold();
                    right.Item().Text($"Datum: {order.CreatedAt.ToLocalTime():dd.MM.yyyy}");
                    right.Item().Text($"Uhrzeit: {order.CreatedAt.ToLocalTime():HH:mm} Uhr");
                    if (!string.IsNullOrEmpty(order.TrackingCode))
                        right.Item().Text($"Tracking: {order.TrackingCode}").FontColor(Colors.Orange.Medium).Bold();
                });
            });

            col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            if (!string.IsNullOrEmpty(order.CustomerName) || !string.IsNullOrEmpty(order.CustomerEmail))
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Kunde").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                        if (!string.IsNullOrEmpty(order.CustomerName))
                            left.Item().Text(order.CustomerName);
                        if (!string.IsNullOrEmpty(order.CustomerEmail))
                            left.Item().Text(order.CustomerEmail).FontSize(9);
                        if (!string.IsNullOrEmpty(order.CustomerPhone))
                            left.Item().Text(order.CustomerPhone).FontSize(9);
                    });

                    if (!string.IsNullOrEmpty(order.DeliveryStreet))
                    {
                        row.ConstantItem(180).AlignRight().Column(right =>
                        {
                            right.Item().Text("Lieferadresse").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            right.Item().Text(order.DeliveryStreet);
                            right.Item().Text($"{order.DeliveryPostalCode} {order.DeliveryCity}");
                        });
                    }
                });

                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            }
        });
    }

    private static void ComposeContent(IContainer container, Order order, PosPayment? payment)
    {
        container.PaddingVertical(10).Column(col =>
        {
            // Items table
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(35);  // Pos
                    columns.ConstantColumn(35);  // Qty
                    columns.RelativeColumn();     // Item name
                    columns.ConstantColumn(70);   // Unit price
                    columns.ConstantColumn(50);   // Tax %
                    columns.ConstantColumn(70);   // Line total
                });

                // Header
                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.Bold().FontSize(9).FontColor(Colors.Grey.Darken1);

                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                        .Text("Pos.").Style(headerStyle);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                        .Text("Mge.").Style(headerStyle);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                        .Text("Bezeichnung").Style(headerStyle);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4).AlignRight()
                        .Text("Einzelpreis").Style(headerStyle);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4).AlignRight()
                        .Text("MwSt.").Style(headerStyle);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4).AlignRight()
                        .Text("Gesamt").Style(headerStyle);
                });

                var items = order.Items.ToList();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bgColor).Padding(4).Text($"{i + 1}");
                    table.Cell().Background(bgColor).Padding(4).Text($"{item.Quantity}x");
                    table.Cell().Background(bgColor).Padding(4).Column(c =>
                    {
                        c.Item().Text(item.ItemName);
                        if (item.IsComboItem)
                            c.Item().Text("Combo").FontSize(8).FontColor(Colors.Orange.Medium);
                    });
                    table.Cell().Background(bgColor).Padding(4).AlignRight()
                        .Text($"{item.UnitPrice:C}");
                    table.Cell().Background(bgColor).Padding(4).AlignRight()
                        .Text($"{item.TaxRate:P0}");
                    table.Cell().Background(bgColor).Padding(4).AlignRight()
                        .Text($"{item.LineTotal:C}").Bold();
                }
            });

            col.Item().PaddingTop(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            // Totals
            col.Item().PaddingTop(10).AlignRight().Width(220).Column(totals =>
            {
                TotalRow(totals, "Zwischensumme", $"{order.SubTotal:C}", false);
                TotalRow(totals, "MwSt.", $"{order.TaxAmount:C}", false);
                if (order.DiscountAmount > 0)
                    TotalRow(totals, "Rabatt", $"-{order.DiscountAmount:C}", false);

                totals.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Gesamt").Bold().FontSize(13);
                    row.ConstantItem(100).AlignRight().Text($"{order.GrandTotal:C}")
                        .Bold().FontSize(13).FontColor(Colors.Orange.Medium);
                });
            });

            // Payment info
            if (payment != null)
            {
                col.Item().PaddingTop(20).Row(row =>
                {
                    row.RelativeItem().Column(info =>
                    {
                        info.Item().Text("Zahlungsart").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                        info.Item().Text(payment.PaymentMethod == PosPaymentMethod.Card ? "Kartenzahlung" : "Barzahlung");
                        info.Item().Text($"Status: {(payment.Status == PosPaymentStatus.Succeeded ? "Bezahlt" : "Ausstehend")}")
                            .FontSize(9);
                    });
                });
            }
        });
    }

    private static void TotalRow(ColumnDescriptor col, string label, string value, bool bold)
    {
        col.Item().PaddingVertical(2).Row(row =>
        {
            var style = bold ? TextStyle.Default.Bold() : TextStyle.Default;
            row.RelativeItem().Text(label).Style(style).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(100).AlignRight().Text(value).Style(style);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(8).AlignCenter()
                .Text("Vielen Dank für deine Bestellung!")
                .FontSize(9).FontColor(Colors.Grey.Medium);
            col.Item().AlignCenter()
                .Text(text =>
                {
                    text.Span("Speise-Direkt — ").FontSize(8).FontColor(Colors.Grey.Lighten1);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Lighten1);
                    text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Lighten1);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Lighten1);
                });
        });
    }
}
