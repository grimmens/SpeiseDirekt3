namespace SpeiseDirekt.ServiceInterface;

/// <summary>
/// Server-side session management for anonymous POS kiosk carts.
/// Cart state lives in IMemoryCache, keyed by session GUID.
/// Only the session ID is stored on the client.
/// </summary>
public interface IPosSessionService
{
    Guid CreateSessionId();
    PosCart GetCart(Guid sessionId);
    void SaveCart(Guid sessionId, PosCart cart);
    void ClearCart(Guid sessionId);
}

/// <summary>
/// Server-side POS cart model stored in memory cache.
/// </summary>
public class PosCart
{
    public List<PosCartItem> Items { get; set; } = new();
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public int TotalQuantity => Items.Sum(i => i.Quantity);

    public int GetQuantity(Guid menuItemId)
    {
        return Items.Where(i => i.MenuItemId == menuItemId && !i.ComboId.HasValue).Sum(i => i.Quantity);
    }

    public void IncrementOrAdd(Guid menuItemId, string name, decimal price, decimal taxRate)
    {
        var existing = Items.FirstOrDefault(i => i.MenuItemId == menuItemId && !i.ComboId.HasValue);
        if (existing != null)
        {
            existing.Quantity++;
            existing.Recalculate();
        }
        else
        {
            Items.Add(new PosCartItem
            {
                MenuItemId = menuItemId,
                Name = name,
                UnitPrice = price,
                TaxRate = taxRate,
                Quantity = 1
            });
        }
        Recalculate();
    }

    public void AddItem(Guid menuItemId, string name, decimal price, decimal taxRate, Guid? comboId, bool isComboTrigger)
    {
        Items.Add(new PosCartItem
        {
            MenuItemId = menuItemId,
            Name = name,
            UnitPrice = price,
            TaxRate = taxRate,
            Quantity = 1,
            ComboId = comboId,
            IsComboTrigger = isComboTrigger
        });
        Recalculate();
    }

    public void UpdateComboQuantity(Guid comboId, int quantity)
    {
        foreach (var item in Items.Where(i => i.ComboId == comboId))
        {
            item.Quantity = quantity;
            item.Recalculate();
        }
        Recalculate();
    }

    public void RemoveCombo(Guid comboId)
    {
        Items.RemoveAll(i => i.ComboId == comboId);
        Recalculate();
    }

    public void Recalculate()
    {
        foreach (var item in Items) item.Recalculate();
        SubTotal = Items.Sum(i => i.LineTotal);
        TaxAmount = Items.Sum(i => i.TaxAmount);
        GrandTotal = SubTotal + TaxAmount;
    }
}

public class PosCartItem
{
    public Guid MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public int Quantity { get; set; } = 1;
    public Guid? ComboId { get; set; }
    public bool IsComboTrigger { get; set; }
    public decimal LineTotal { get; set; }
    public decimal TaxAmount { get; set; }

    public void Recalculate()
    {
        LineTotal = Math.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
        TaxAmount = Math.Round(LineTotal * TaxRate, 2, MidpointRounding.AwayFromZero);
    }
}
