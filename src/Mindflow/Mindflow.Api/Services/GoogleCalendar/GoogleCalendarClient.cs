using System.Net;
using System.Text.Json;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using Mindflow.Api.Models;

namespace Mindflow.Api.Services.GoogleCalendar;

public class GoogleCalendarClient(
    IOptions<GoogleCalendarOptions> options,
    IConfiguration configuration,
    IGoogleTokenProtector tokenProtector) : IGoogleCalendarClient
{
    private const string ApplicationName = "Mindflow";
    private const string PrimaryCalendar = "primary";

    private readonly GoogleCalendarOptions _options = options.Value;

    private string ClientId =>
        configuration["Google:ClientId"]
        ?? throw new InvalidOperationException("Google:ClientId is not configured.");

    private string ClientSecret =>
        configuration["Google:ClientSecret"]
        ?? throw new InvalidOperationException("Google:ClientSecret is not configured.");

    private string RedirectUri =>
        _options.RedirectUri
        ?? throw new InvalidOperationException("Google:Calendar:RedirectUri is not configured.");

    public string BuildConsentUrl(string state)
    {
        var request = new GoogleAuthorizationCodeRequestUrl(new Uri("https://accounts.google.com/o/oauth2/v2/auth"))
        {
            ClientId = ClientId,
            Scope = $"{CalendarService.Scope.Calendar} email",
            RedirectUri = RedirectUri,
            AccessType = "offline",
            Prompt = "consent",
            State = state
        };
        return request.Build().ToString();
    }

    public async Task<GoogleTokenExchangeResult> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var flow = CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync("user", code, RedirectUri, ct);

        var email = ExtractEmail(token.IdToken)
            ?? throw new InvalidOperationException("Google did not return an account email.");

        DateTimeOffset? expiresAt = token.ExpiresInSeconds is long seconds
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : null;

        return new GoogleTokenExchangeResult(token.AccessToken, token.RefreshToken, expiresAt, email);
    }

    public async Task<string> CreateDedicatedCalendarAsync(GoogleCalendarConnection connection, string calendarName, CancellationToken ct)
    {
        using var service = BuildService(connection);
        var created = await service.Calendars.Insert(new Calendar { Summary = calendarName }).ExecuteAsync(ct);
        return created.Id;
    }

    public async Task<string> UpsertEventAsync(GoogleCalendarConnection connection, CalendarBlock block, CancellationToken ct)
    {
        using var service = BuildService(connection);
        var calendarId = block.GoogleCalendarId ?? connection.DedicatedCalendarId;

        var body = new Event
        {
            Summary = string.IsNullOrWhiteSpace(block.Title) ? "Mindflow" : block.Title,
            Start = new EventDateTime { DateTimeDateTimeOffset = block.StartAt },
            End = new EventDateTime { DateTimeDateTimeOffset = block.StartAt.AddMinutes(block.DurationMinutes) }
        };

        if (string.IsNullOrWhiteSpace(block.ExternalEventId))
        {
            var created = await service.Events.Insert(body, calendarId).ExecuteAsync(ct);
            return created.Id;
        }

        var updated = await service.Events.Patch(body, calendarId, block.ExternalEventId).ExecuteAsync(ct);
        return updated.Id;
    }

    public async Task DeleteEventAsync(GoogleCalendarConnection connection, string calendarId, string externalEventId, CancellationToken ct)
    {
        using var service = BuildService(connection);
        try
        {
            await service.Events.Delete(calendarId, externalEventId).ExecuteAsync(ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // already gone in Google — nothing to do
        }
    }

    public async Task<GoogleSyncResult> ListChangesAsync(GoogleCalendarConnection connection, string? syncToken, CancellationToken ct)
    {
        using var service = BuildService(connection);
        var calendarId = connection.SourceCalendarId ?? PrimaryCalendar;

        var changes = new List<GoogleEventChange>();
        string? pageToken = null;
        string? nextSyncToken = null;

        do
        {
            var request = service.Events.List(calendarId);
            request.SingleEvents = true;
            request.ShowDeleted = true;
            request.MaxResults = 250;
            request.PageToken = pageToken;

            if (string.IsNullOrWhiteSpace(syncToken))
            {
                request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-_options.SyncWindowPastDays);
                request.TimeMaxDateTimeOffset = DateTimeOffset.UtcNow.AddDays(_options.SyncWindowFutureDays);
            }
            else
            {
                request.SyncToken = syncToken;
            }

            Events response;
            try
            {
                response = await request.ExecuteAsync(ct);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                throw new GoogleSyncTokenExpiredException();
            }

            foreach (var item in response.Items ?? [])
            {
                var change = MapChange(item);
                if (change is not null) changes.Add(change);
            }

            pageToken = response.NextPageToken;
            nextSyncToken = response.NextSyncToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return new GoogleSyncResult(changes, nextSyncToken);
    }

    public async Task<GoogleWatchResult> StartWatchAsync(
        GoogleCalendarConnection connection, string channelId, string channelToken, string webhookUrl, CancellationToken ct)
    {
        using var service = BuildService(connection);
        var calendarId = connection.SourceCalendarId ?? PrimaryCalendar;

        var channel = new Channel
        {
            Id = channelId,
            Type = "web_hook",
            Address = webhookUrl,
            Token = channelToken
        };

        var result = await service.Events.Watch(channel, calendarId).ExecuteAsync(ct);
        DateTimeOffset? expiresAt = result.Expiration is long ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

        return new GoogleWatchResult(result.ResourceId, expiresAt);
    }

    public async Task StopWatchAsync(GoogleCalendarConnection connection, string channelId, string resourceId, CancellationToken ct)
    {
        using var service = BuildService(connection);
        try
        {
            await service.Channels.Stop(new Channel { Id = channelId, ResourceId = resourceId }).ExecuteAsync(ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // channel already expired/stopped
        }
    }

    private static GoogleEventChange? MapChange(Event item)
    {
        if (string.Equals(item.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            return new GoogleEventChange(item.Id, IsDeleted: true, null, default, 0);

        // Only timed events land on the time grid; all-day events (Start.Date) are skipped.
        var start = item.Start?.DateTimeDateTimeOffset;
        var end = item.End?.DateTimeDateTimeOffset;
        if (start is null || end is null)
            return null;

        var duration = (int)(end.Value - start.Value).TotalMinutes;
        if (duration <= 0) duration = 30;

        return new GoogleEventChange(item.Id, IsDeleted: false, item.Summary, start.Value, duration);
    }

    private GoogleAuthorizationCodeFlow CreateFlow() =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
            Scopes = [CalendarService.Scope.Calendar]
        });

    private CalendarService BuildService(GoogleCalendarConnection connection)
    {
        var token = new TokenResponse
        {
            RefreshToken = tokenProtector.Unprotect(connection.RefreshTokenEncrypted),
            AccessToken = connection.AccessTokenEncrypted is null ? null : tokenProtector.Unprotect(connection.AccessTokenEncrypted)
        };

        var credential = new UserCredential(CreateFlow(), connection.UserId.ToString(), token);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });
    }

    private static string? ExtractEmail(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
