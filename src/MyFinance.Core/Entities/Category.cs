using MyFinance.Core.Enums;

namespace MyFinance.Core.Entities;

/// <summary>
/// A spending or income classification. Two levels deep, matching how Microsoft Money
/// renders them: a parent "Bills" with a child "Mobile phone" displays as
/// "Bills : Mobile phone".
/// </summary>
public class Category
{
    public int Id { get; set; }

    /// <summary>Leaf name only, e.g. "Mobile phone" — not the full path.</summary>
    public required string Name { get; set; }

    public int? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = [];

    public CategoryKind Kind { get; set; }

    /// <summary>Included in the tax-related transactions report.</summary>
    public bool IsTaxRelated { get; set; }

    /// <summary>Hidden from pickers but kept so historical transactions still resolve.</summary>
    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Display path, e.g. "Bills : Mobile phone".</summary>
    public string FullName => Parent is null ? Name : $"{Parent.Name} : {Name}";
}
