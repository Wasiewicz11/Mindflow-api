namespace Mindflow.Api.Services.Integrations;

/// <summary>The space an integration token is limited to; null for user sessions and unrestricted tokens.</summary>
public interface IIntegrationSpaceScope
{
    Guid? SpaceId { get; }
}
