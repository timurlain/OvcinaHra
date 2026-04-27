namespace OvcinaHra.Shared.Utils;

public static class PriceRules
{
    /// <summary>
    /// Server canonical rule: an item is "sold" iff its price is greater than zero.
    /// Setting <c>Price = 0</c> or <c>null</c> auto-clears <c>IsSold</c> to <c>false</c>;
    /// setting <c>Price &gt; 0</c> auto-sets <c>IsSold</c> to <c>true</c>.
    /// Use this single helper everywhere the rule is applied so the canon stays single.
    /// </summary>
    public static bool DeriveIsSold(int? price) => price is > 0;
}
