using Mindflow.Api.Extensions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;

builder.Services.AddMindflowDatabase(config);
builder.Services.AddMindflowAuth(config);
builder.Services.AddMindflowRepositories();
builder.Services.AddMindflowServices();
builder.Services.AddSignalR();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
