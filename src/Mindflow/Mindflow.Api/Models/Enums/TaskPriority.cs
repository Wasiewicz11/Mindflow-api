using System.Text.Json.Serialization;

namespace Mindflow.Api.Models.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))]
public enum TaskPriority
{
    P1,
    P2,
    P3,
    P4
}
