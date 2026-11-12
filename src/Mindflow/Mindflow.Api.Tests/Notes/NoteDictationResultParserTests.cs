using System.Text.Json;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Notes;

namespace Mindflow.Api.Tests.Notes;

public class NoteDictationResultParserTests
{
    private static readonly DictationContext Context = new(new DateOnly(2026, 11, 12), new TimeOnly(10, 0), "Europe/Warsaw", []);

    [Fact]
    public void Missing_title_falls_back_to_the_date_and_tags_are_capped()
    {
        var result = NoteDictationResultParser.Parse(
            """{"title":"  ","tags":["A","b","#c","d","e","f"],"markdown":"<p>Tekst</p>","eventDate":"2010-01-01","tasks":[]}""",
            Context,
            30);

        Assert.Equal("Notatka z 12.11.2026", result!.Title);
        Assert.Equal(["a", "b", "c", "d", "e"], result.Tags);
        Assert.Equal("Tekst", result.Markdown);
        Assert.Null(result.EventDate);
    }

    [Fact]
    public void Tag_shaped_html_is_removed_but_comparisons_and_autolinks_survive()
    {
        var result = NoteDictationResultParser.Parse(
            "{\"title\":\"T\",\"tags\":[],\"markdown\":\"koszt <1000 zł, zysk >500 <https://x.pl> <b>tak</b><br/>\",\"tasks\":[]}",
            Context,
            30);

        Assert.Equal("koszt <1000 zł, zysk >500 <https://x.pl> tak", result!.Markdown);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>x", "alert(1)x")]
    [InlineData("<img src=x onerror=alert(1)>x", "x")]
    [InlineData("<img/src=x/onerror=alert(1)>x", "x")]
    [InlineData("<svg/onload=alert(1)>x", "x")]
    [InlineData("<div>a</div>", "a")]
    [InlineData("a<br/>b", "ab")]
    [InlineData("<b>a</b>", "a")]
    [InlineData("<https://x.pl>", "<https://x.pl>")]
    [InlineData("koszt <1000 zł, zysk >500", "koszt <1000 zł, zysk >500")]
    public void Html_tags_are_stripped_in_every_form(string markdown, string expected)
    {
        var json = JsonSerializer.Serialize(new { title = "T", tags = Array.Empty<string>(), markdown, tasks = Array.Empty<string>() });

        Assert.Equal(expected, NoteDictationResultParser.Parse(json, Context, 30)!.Markdown);
    }

    [Fact]
    public void Nul_characters_are_removed_from_every_text_field()
    {
        var result = NoteDictationResultParser.Parse(
            "{\"transcript\":\"a\\u0000b\",\"title\":\"T\\u0000t\",\"tags\":[\"x\\u0000y\"],\"markdown\":\"m\\u0000d\",\"tasks\":[]}",
            Context,
            30);

        Assert.Equal("Tt", result!.Title);
        Assert.Equal(["xy"], result.Tags);
        Assert.Equal("md", result.Markdown);
        Assert.Equal("ab", result.Transcript);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nie json")]
    [InlineData("[]")]
    public void Answer_that_is_not_a_json_object_is_rejected(string? raw) =>
        Assert.Null(NoteDictationResultParser.Parse(raw, Context, 30));

    [Fact]
    public void Openai_request_names_the_note_schema()
    {
        var request = OpenAiDictationEngine.BuildRequest("gpt-4o-mini", "Notatka", Context, NoteDictationPrompts.Instance);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var schema = doc.RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.Equal("dictated_note", schema.GetProperty("name").GetString());
        Assert.True(schema.GetProperty("schema").GetProperty("properties").TryGetProperty("markdown", out _));
    }
}
