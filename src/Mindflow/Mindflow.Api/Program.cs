using System.Text.Json.Serialization;
using Mindflow.Api.Extensions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;

builder.Services.AddMindflowDatabase(config);
builder.Services.AddMindflowAuth(config);
builder.Services.AddMindflowStorage(config);
builder.Services.AddMindflowRepositories();
builder.Services.AddMindflowServices();
builder.Services.AddMindflowAi(config);
builder.Services.AddSignalR();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var frontendUrl = string.IsNullOrWhiteSpace(config["Cors:FrontendUrl"])
    ? "http://localhost:5173"
    : config["Cors:FrontendUrl"]!;
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<ApiExceptionHandlingMiddleware>();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TasksHub>("/hubs/tasks");

app.Run();
