using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class NotificationInboxApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Inbox = "/notifications/inbox";

    [LocalDatabaseFact]
    public async Task Legacy_inbox_still_returns_a_plain_list_newest_first()
    {
        var (client, userId) = await CreateClientAsync();
        var ids = await SeedAsync(userId, count: 3, readCount: 1);

        var items = await client.GetFromJsonAsync<List<NotificationInboxItemResponse>>(Inbox, MindflowApiFactory.Json);

        Assert.Equal(ids, items!.Select(item => item.Id).ToList());
    }

    [LocalDatabaseFact]
    public async Task Page_returns_slice_with_total_and_unread_counts()
    {
        var (client, userId) = await CreateClientAsync();
        var ids = await SeedAsync(userId, count: 5, readCount: 2);

        var first = await GetPageAsync(client, "limit=2&offset=0");
        var last = await GetPageAsync(client, "limit=2&offset=4");

        Assert.Equal(ids[..2], first.Items.Select(item => item.Id).ToList());
        Assert.Equal((5, 3), (first.Total, first.Unread));
        Assert.Equal([ids[4]], last.Items.Select(item => item.Id).ToList());
    }

    [LocalDatabaseFact]
    public async Task Page_defaults_to_thirty_items()
    {
        var (client, userId) = await CreateClientAsync();
        await SeedAsync(userId, count: 31);

        var page = await GetPageAsync(client, "");

        Assert.Equal(30, page.Items.Count);
        Assert.Equal(31, page.Total);
    }

    [LocalDatabaseFact]
    public async Task Page_rejects_invalid_limit_and_offset()
    {
        var (client, _) = await CreateClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Inbox}/page?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Inbox}/page?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Inbox}/page?offset=-1")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Page_shows_only_the_callers_notifications()
    {
        var (client, userId) = await CreateClientAsync();
        var (_, otherId) = await CreateClientAsync();
        var ids = await SeedAsync(userId, count: 1);
        await SeedAsync(otherId, count: 4);

        var page = await GetPageAsync(client, "");

        Assert.Equal(ids, page.Items.Select(item => item.Id).ToList());
        Assert.Equal((1, 1), (page.Total, page.Unread));
    }

    [LocalDatabaseFact]
    public async Task Single_notification_can_be_marked_unread_again()
    {
        var (client, userId) = await CreateClientAsync();
        var id = (await SeedAsync(userId, count: 1))[0];

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Inbox}/{id}/read", null)).StatusCode);
        Assert.Equal(0, (await GetPageAsync(client, "")).Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Inbox}/{id}/unread", null)).StatusCode);
        Assert.Null((await GetPageAsync(client, "")).Items.Single().ReadAt);
    }

    [LocalDatabaseFact]
    public async Task Marking_unread_does_not_touch_other_users_notifications()
    {
        var (client, _) = await CreateClientAsync();
        var (_, otherId) = await CreateClientAsync();
        var otherIds = await SeedAsync(otherId, count: 1, readCount: 1);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Inbox}/{otherIds[0]}/unread", null)).StatusCode);

        Assert.All(await LoadAsync(otherId), item => Assert.NotNull(item.ReadAt));
    }

    [LocalDatabaseFact]
    public async Task Read_all_and_unread_all_flip_every_notification_of_the_caller()
    {
        var (client, userId) = await CreateClientAsync();
        var (_, otherId) = await CreateClientAsync();
        await SeedAsync(userId, count: 4, readCount: 1);
        await SeedAsync(otherId, count: 2, readCount: 1);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Inbox}/read-all", null)).StatusCode);
        Assert.All(await LoadAsync(userId), item => Assert.NotNull(item.ReadAt));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Inbox}/unread-all", null)).StatusCode);
        Assert.All(await LoadAsync(userId), item => Assert.Null(item.ReadAt));

        Assert.Equal(1, (await LoadAsync(otherId)).Count(item => item.ReadAt == null));
    }

    [LocalDatabaseFact]
    public async Task Read_all_keeps_the_original_read_time_of_already_read_items()
    {
        var (client, userId) = await CreateClientAsync();
        await SeedAsync(userId, count: 2, readCount: 1);
        var before = (await LoadAsync(userId)).Single(item => item.ReadAt != null);

        await client.PutAsync($"{Inbox}/read-all", null);

        var after = (await LoadAsync(userId)).Single(item => item.Id == before.Id);
        Assert.Equal(before.ReadAt, after.ReadAt);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return (factory.CreateUserClient(user.Id), user.Id);
    }

    private static async Task<NotificationInboxPageResponse> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Inbox}/page?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationInboxPageResponse>(MindflowApiFactory.Json))!;
    }

    /// <summary>Seeds notifications and returns their ids newest first; the oldest <paramref name="readCount"/> are read.</summary>
    private async Task<List<Guid>> SeedAsync(Guid userId, int count, int readCount = 0)
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var items = Enumerable.Range(0, count)
            .Select(i => new NotificationInboxItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = NotificationInboxKind.MorningBrief,
                Title = $"Brief {i}",
                Body = "Treść",
                CreatedAt = start.AddMinutes(i),
                ReadAt = i < readCount ? start.AddMinutes(i + 1) : null
            })
            .ToList();

        await factory.WithDbAsync(async db =>
        {
            db.NotificationInboxItems.AddRange(items);
            await db.SaveChangesAsync();
        });

        return items.OrderByDescending(item => item.CreatedAt).Select(item => item.Id).ToList();
    }

    private Task<List<NotificationInboxItem>> LoadAsync(Guid userId) =>
        factory.WithDbAsync(db => db.NotificationInboxItems.AsNoTracking().Where(item => item.UserId == userId).ToListAsync());
}
