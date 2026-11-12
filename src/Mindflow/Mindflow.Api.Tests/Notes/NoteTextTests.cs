using Mindflow.Api.Services.Notes;

namespace Mindflow.Api.Tests.Notes;

public class NoteTextTests
{
    [Fact]
    public void Preview_drops_markdown_syntax_code_and_rules()
    {
        const string markdown = "# Tytuł\n\n- [x] **Zrobione** zadanie\n> cytat\n```\nkod\n```\n---\n1. pierwszy [link](https://x.pl) i `kod`";

        Assert.Equal("Tytuł Zrobione zadanie cytat pierwszy link i kod", NoteText.Preview(markdown));
    }

    [Fact]
    public void Preview_is_cut_to_the_limit_with_an_ellipsis()
    {
        var preview = NoteText.Preview(new string('a', 300));

        Assert.Equal(NoteText.PreviewLength, preview.Length);
        Assert.EndsWith("…", preview);
        Assert.Equal("", NoteText.Preview("   \n  "));
        Assert.Equal("", NoteText.Preview(null));
    }

    [Fact]
    public void Tags_are_trimmed_lowercased_unique_and_limited()
    {
        var tags = NoteText.NormalizeTags([" Praca", "praca", "", "  ", new string('x', 50)]);

        Assert.Equal(["praca", new string('x', 40)], tags);
        Assert.Equal(20, NoteText.NormalizeTags(Enumerable.Range(1, 25).Select(i => $"t{i}")).Count);
        Assert.Empty(NoteText.NormalizeTags(null));
    }

    [Fact]
    public void Tag_cut_does_not_leave_a_lone_surrogate()
    {
        var tag = NoteText.NormalizeTags([new string('a', 39) + "😀"])[0];

        Assert.False(char.IsHighSurrogate(tag[^1]));
        Assert.True(tag.Length <= NoteText.MaxTagLength);
    }

    [Fact]
    public void Preview_cut_does_not_leave_a_lone_surrogate()
    {
        var preview = NoteText.Preview(new string('a', 158) + "😀😀");

        Assert.EndsWith("…", preview);
        Assert.False(char.IsHighSurrogate(preview[^2]));
        Assert.True(preview.Length <= NoteText.PreviewLength);
    }
}
