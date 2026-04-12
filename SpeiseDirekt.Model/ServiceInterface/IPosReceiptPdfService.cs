using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IPosReceiptPdfService
{
    byte[] Generate(Order order, PosPayment? payment);
}
