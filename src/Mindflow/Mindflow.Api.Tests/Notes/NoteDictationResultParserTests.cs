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
