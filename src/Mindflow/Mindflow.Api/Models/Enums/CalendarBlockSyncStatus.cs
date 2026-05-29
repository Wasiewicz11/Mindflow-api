using System.Text.Json.Serialization;

namespace Mindflow.Api.Models.Enums;

public enum CalendarBlockSyncStatus
{
    Local = 0,
    Synced = 1,
    Conflict = 2
}
