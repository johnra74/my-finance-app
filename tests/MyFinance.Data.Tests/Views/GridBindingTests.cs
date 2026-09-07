using System.Reflection;
using System.Xml.Linq;
using MyFinance.Data.Services;
using MyFinance.Data.Tests.Sources;

namespace MyFinance.Data.Tests.Views;

/// <summary>
/// The grids show projections, and must not try to write back into them.
/// </summary>
/// <remarks>
/// <para>
/// The diagnostics log caught two versions of one mistake on the categories page. A check-box
/// column bound to <c>IsArchived</c> threw — "a TwoWay or OneWayToSource binding cannot work
/// on the read-only property" — because <see cref="DataGridCheckBoxColumn"/> binds two way
/// unless told otherwise and the read model has no setter. The column beside it bound to a
/// property that <em>did</em> have a setter, so it threw nothing: it quietly edited a detached
/// entity nobody was going to save, which is the worse of the two.
/// </para>
/// <para>
/// So the rule enforced here is about the whole class and not those two columns: an editable
/// check-box column has to say it is editable. Silence is how this got in.
/// </para>
/// </remarks>
public sealed class GridBindingTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [SourceTreeFact]
    public void Every_check_box_column_declares_whether_it_can_be_edited()
    {
        List<string> undeclared = [];

        foreach (string file in MarkupFiles())
        {
            foreach (XElement column in XDocument.Load(file)
                         .Descendants(Presentation + "DataGridCheckBoxColumn"))
            {
                string binding = (string?)column.Attribute("Binding") ?? string.Empty;
                bool declared =
                    string.Equals((string?)column.Attribute("IsReadOnly"), "True", StringComparison.Ordinal)
                    || binding.Contains("Mode=", StringComparison.Ordinal);

                if (!declared)
                {
                    undeclared.Add(
                        $"{Path.GetFileName(file)}: {(string?)column.Attribute("Header") ?? binding}");
                }
            }
        }

        undeclared.ShouldBeEmpty(
            "A DataGridCheckBoxColumn binds two way by default. Say IsReadOnly=\"True\" or "
            + "Mode=OneWay for a column over a read model, or Mode=TwoWay where the edit is "
            + "genuinely wanted and goes through a service.");
    }

    [SourceTreeFact]
    public void The_categories_grid_binds_its_check_boxes_one_way()
    {
        string file = Path.Combine(
            SourceTree.Root!, "src", "MyFinance.App", "Views", "Pages", "CategoriesPage.xaml");

        XElement[] columns = [.. XDocument.Load(file)
            .Descendants(Presentation + "DataGridCheckBoxColumn")];

        columns.Length.ShouldBe(2);

        foreach (XElement column in columns)
        {
            ((string?)column.Attribute("IsReadOnly")).ShouldBe("True");
            ((string?)column.Attribute("Binding") ?? string.Empty).ShouldContain("Mode=OneWay");
        }
    }

    [Fact]
    public void The_category_list_item_is_a_read_model()
    {
        string[] projected =
        [
            nameof(CategoryListItem.Id),
            nameof(CategoryListItem.IsParent),
            nameof(CategoryListItem.Kind),
            nameof(CategoryListItem.IsArchived),
            nameof(CategoryListItem.FullName),
            nameof(CategoryListItem.UseCount),
        ];

        foreach (string name in projected)
        {
            PropertyInfo property = typeof(CategoryListItem).GetProperty(name)!;

            // Get-only or init-only; either way there is nothing a binding can write back
            // into after the projection is built. Adding a plain setter here would silence
            // the binding error rather than fix it, and would leave a tick that is accepted
            // and then thrown away.
            IsSettableAfterConstruction(property).ShouldBeFalse(
                $"{name} is a projection. Changes go through CategoryService, not the grid.");
        }
    }

    private static bool IsSettableAfterConstruction(PropertyInfo property) =>
        property.SetMethod is MethodInfo setter
        && !setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private static IEnumerable<string> MarkupFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(SourceTree.Root!, "src", "MyFinance.App", "Views"),
            "*.xaml",
            SearchOption.AllDirectories);
}
