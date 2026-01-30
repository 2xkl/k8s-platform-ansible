using System.Security.Claims;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var keycloakUrl = builder.Configuration["Keycloak:Authority"]
    ?? "http://keycloak.keycloak.svc.cluster.local:8080/realms/demo";
var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"]
    ?? "demo-kafka-kafka-bootstrap.kafka.svc.cluster.local:9092";
var kafkaTopic = "demo-messages";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakUrl;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidIssuer = keycloakUrl
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// --- Public endpoints ---

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "demo-api"
}));

// --- Protected endpoints ---

app.MapGet("/api/userinfo", (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(new
    {
        username = user.FindFirstValue("preferred_username") ?? user.Identity?.Name,
        email = user.FindFirstValue("email"),
        roles = user.FindAll("realm_access").Select(c => c.Value),
        claims
    });
}).RequireAuthorization();

app.MapPost("/api/messages", async (MessageRequest request) =>
{
    var config = new ProducerConfig { BootstrapServers = kafkaBootstrap };

    using var producer = new ProducerBuilder<string, string>(config).Build();
    var key = $"api-{DateTime.UtcNow:HHmmss}";

    var result = await producer.ProduceAsync(kafkaTopic, new Message<string, string>
    {
        Key = key,
        Value = request.Message
    });

    return Results.Ok(new
    {
        status = "sent",
        topic = result.Topic,
        partition = result.Partition.Value,
        offset = result.Offset.Value,
        timestamp = result.Timestamp.UtcDateTime
    });
}).RequireAuthorization();

app.MapGet("/api/messages", (int? count) =>
{
    var maxMessages = count ?? 10;

    var config = new ConsumerConfig
    {
        BootstrapServers = kafkaBootstrap,
        GroupId = $"api-reader-{Guid.NewGuid():N[..8]}",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
    };

    var messages = new List<object>();

    using var consumer = new ConsumerBuilder<string, string>(config).Build();
    consumer.Subscribe(kafkaTopic);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    try
    {
        while (messages.Count < maxMessages)
        {
            var result = consumer.Consume(cts.Token);
            messages.Add(new
            {
                key = result.Message.Key,
                value = result.Message.Value,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                timestamp = result.Message.Timestamp.UtcDateTime
            });
        }
    }
    catch (OperationCanceledException) { }

    consumer.Close();

    return Results.Ok(new { total = messages.Count, messages });
}).RequireAuthorization();

app.Run();

record MessageRequest(string Message);
