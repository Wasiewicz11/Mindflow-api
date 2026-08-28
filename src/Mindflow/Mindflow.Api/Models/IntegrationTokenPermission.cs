using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class IntegrationTokenPermission
{
    public Guid Id { get; set; }
    public Guid IntegrationTokenId { get; set; }
    public IntegrationToken? IntegrationToken { get; set; }
    public IntegrationTokenScope Scope { get; set; }
}
