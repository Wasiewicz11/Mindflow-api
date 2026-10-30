using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Tests;

public class ExternalTaskReferenceRulesTests
{
    [Theory]
    [InlineData("rendmark")]
    [InlineData("jira-cloud")]
    [InlineData("a1")]
    public void Valid_source_passes(string source)
    {
        Assert.Empty(ExternalTaskReferenceRules.Validate(source, "42", null, false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Rendmark")]
    [InlineData("rend_mark")]
    [InlineData("rendmark\n")]
    [InlineData("a-very-long-source-name-that-exceeds-forty-chars")]
    public void Invalid_source_is_rejected(string source)
    {
        Assert.Contains("source", ExternalTaskReferenceRules.Validate(source, "42", null, false).Keys);
    }

    [Fact]
    public void External_id_longer_than_200_characters_is_rejected()
    {
        var errors = ExternalTaskReferenceRules.Validate("rendmark", new string('x', 201), null, false);

        Assert.Contains("externalId", errors.Keys);
    }

    [Theory]
    [InlineData("http://app.rendmark.pl/tasks/1")]
    [InlineData("http://localhost:4200/tasks/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/tasks/1")]
    public void Non_https_url_is_rejected_outside_development(string url)
    {
        Assert.Contains("externalUrl", ExternalTaskReferenceRules.Validate("rendmark", "1", url, false).Keys);
    }

    [Fact]
    public void Localhost_http_url_is_allowed_in_development()
    {
        Assert.Empty(ExternalTaskReferenceRules.Validate("rendmark", "1", "http://localhost:4200/tasks/1", true));
        Assert.Contains("externalUrl", ExternalTaskReferenceRules.Validate("rendmark", "1", "http://rendmark.pl/tasks/1", true).Keys);
    }

    [Fact]
    public void Https_url_is_allowed()
    {
        Assert.Empty(ExternalTaskReferenceRules.Validate("rendmark", "1", "https://app.rendmark.pl/tasks/1", false));
    }
}
