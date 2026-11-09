using Mindflow.Api.Authentication;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationSpaceScope(IHttpContextAccessor httpContextAccessor) : IIntegrationSpaceScope
{
    public Guid? SpaceId => httpContextAccessor.HttpContext?.User.GetIntegrationSpaceId();
}
