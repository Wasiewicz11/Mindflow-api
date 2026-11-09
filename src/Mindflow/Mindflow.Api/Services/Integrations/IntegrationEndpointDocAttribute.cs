namespace Mindflow.Api.Services.Integrations;

[AttributeUsage(AttributeTargets.Method)]
public class IntegrationEndpointDocAttribute(string scope, string description) : Attribute
{
    public string Scope => scope;
    public string Description => description;
}
