using MyFinance.Core.Help;

namespace MyFinance.Core.Tests.Help;

/// <summary>
/// The topic-to-page map, checked for the things that would make a Help key do nothing.
/// </summary>
/// <remarks>
/// That the pages themselves exist is asserted from the data test project, which can read the
/// repository's own documentation folder. This is the half that needs no files at all.
/// </remarks>
public sealed class HelpTopicTests
{
    public static TheoryData<HelpTopic> AllTopics()
    {
        var data = new TheoryData<HelpTopic>();

        foreach (HelpTopic topic in HelpTopics.All)
        {
            data.Add(topic);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_topic_names_a_page(HelpTopic topic) =>
        HelpTopics.PageFor(topic).ShouldNotBeNullOrWhiteSpace();

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_page_path_is_relative_and_html(HelpTopic topic)
    {
        string page = HelpTopics.PageFor(topic);

        // Combined with the folder the documentation was unpacked into, so a path that could
        // climb out of it or start at a root would open something else entirely.
        page.ShouldEndWith(".html");
        page.ShouldNotStartWith("/");
        page.ShouldNotContain("..");
        page.ShouldNotContain("\\", Case.Sensitive);
        Path.IsPathRooted(page).ShouldBeFalse();
    }

    [Fact]
    public void An_unmapped_topic_is_refused_rather_than_sent_to_the_contents()
    {
        // Adding a topic and forgetting the page would otherwise open the front page and look
        // as though it had worked, which is the failure nobody reports.
        Should.Throw<ArgumentOutOfRangeException>(() => HelpTopics.PageFor((HelpTopic)9999));
    }

    [Fact]
    public void The_contents_is_the_default_topic() =>
        // What a page that has not declared a topic falls back to, so it must be first.
        ((int)HelpTopic.Contents).ShouldBe(0);
}
