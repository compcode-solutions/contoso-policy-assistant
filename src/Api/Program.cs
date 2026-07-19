using System.Security.Claims;
using System.Text;
using Contoso.PolicyAssistant.Api.Features.Agent;
using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Auth;
using Contoso.PolicyAssistant.Api.Features.Logging;
using Contoso.PolicyAssistant.Api.Features.Policies;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Contoso Policy Assistant",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT from POST /api/auth/login. Paste: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key must be set (see appsettings.Development.json).");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = signingKey,
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("SupervisorOrAdmin", policy => policy.RequireRole("Supervisor", "Admin"));
});

var (embedClient, chatClient, aiMode) = AiClientFactory.Create(builder.Configuration);
builder.Services.AddSingleton(embedClient);
builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton<InMemoryVectorStore>();
builder.Services.AddSingleton<IngestService>();
builder.Services.AddSingleton<IAiCallLogger, AiCallLogger>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PolicyCatalog>();
builder.Services.AddSingleton<AskQuestionHandler>();
builder.Services.AddSingleton<PendingApprovalStore>();
builder.Services.AddSingleton<TicketStore>();
builder.Services.AddSingleton<AgentAskHandler>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

var ragOpts = app.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
if (ragOpts.AutoIngestOnStartup)
{
    using var scope = app.Services.CreateScope();
    var ingest = scope.ServiceProvider.GetRequiredService<IngestService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        var result = ingest.IngestAsync().GetAwaiter().GetResult();
        logger.LogInformation(
            "Auto-ingest complete: {Chunks} chunks from {Docs} docs via {Provider} (mode={Mode})",
            result.ChunkCount,
            result.DocumentCount,
            result.Provider,
            aiMode);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Auto-ingest failed — call POST /api/ingest after fixing AI config.");
    }
}

app.MapGet("/health", (InMemoryVectorStore store, TicketStore tickets, PendingApprovalStore pending) => Results.Ok(new
{
    status = "ok",
    service = "Contoso.PolicyAssistant.Api",
    utc = DateTime.UtcNow,
    aiMode,
    indexChunks = store.Count,
    indexProvider = store.Provider,
    tickets = tickets.List().Count,
    pendingApprovals = pending.ListPending().Count
}))
.WithName("Health")
.WithTags("Ops")
.AllowAnonymous();

app.MapGet("/api/info", (InMemoryVectorStore store) => Results.Ok(new
{
    name = "Contoso Policy Assistant",
    phase = "Day 5 — Docker, CI, consulting wrap",
    next = "Customer deploy: Entra + AI Search + App Insights (see docs/AZURE-TARGET-ARCHITECTURE.md)",
    aiMode,
    indexChunks = store.Count,
    azureMapping =
        "Entra + AI Search ACL; create_ticket → Logic Apps/ServiceNow with approval; App Insights for request + ai-*.jsonl traces."
}))
.WithName("Info")
.WithTags("Ops")
.AllowAnonymous();

app.MapPost("/api/auth/login", (LoginRequest? body, TokenService tokens) =>
{
    if (body is null
        || string.IsNullOrWhiteSpace(body.Username)
        || string.IsNullOrWhiteSpace(body.Password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["username"] = ["Username and password are required."]
        });
    }

    var user = DevUsers.Find(body.Username.Trim(), body.Password);
    if (user is null)
    {
        return Results.Json(
            new { title = "Invalid credentials", detail = "Unknown user or bad password." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(tokens.CreateToken(user));
})
.WithName("Login")
.WithTags("Auth")
.AllowAnonymous()
.Produces<LoginResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/api/policies", (HttpContext http, PolicyCatalog catalog) =>
{
    var roles = UserRoles.From(http.User);
    var visible = catalog.GetVisibleTo(roles);
    return Results.Ok(new
    {
        roles,
        totalInCatalog = catalog.All.Count,
        visibleCount = visible.Count,
        policies = visible.Select(p => new PolicySummary
        {
            Id = p.Id,
            Title = p.Title,
            AllowedRoles = p.AllowedRoles
        })
    });
})
.WithName("ListPolicies")
.WithTags("Policies")
.RequireAuthorization();

app.MapPost("/api/ingest", async (IngestService ingest, CancellationToken ct) =>
{
    var result = await ingest.IngestAsync(ct);
    return Results.Ok(result);
})
.WithName("IngestPolicies")
.WithTags("Rag")
.RequireAuthorization("AdminOnly")
.Produces<IngestResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/questions", async (
    AskQuestionRequest? body,
    AskQuestionHandler handler,
    HttpContext http,
    CancellationToken ct) =>
{
    var errors = AskQuestionValidator.Validate(body);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var roles = UserRoles.From(http.User);
    var response = await handler.HandleAsync(body!, roles, ct);
    return Results.Ok(response);
})
.WithName("AskQuestion")
.WithTags("Ask")
.RequireAuthorization()
.Produces<AskQuestionResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/api/agent/ask", async (
    AskQuestionRequest? body,
    AgentAskHandler agent,
    HttpContext http,
    CancellationToken ct) =>
{
    var errors = AskQuestionValidator.Validate(body);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var roles = UserRoles.From(http.User);
    var username = http.User.Identity?.Name
        ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    var response = await agent.HandleAsync(body!, username, roles, ct);
    return Results.Ok(response);
})
.WithName("AgentAsk")
.WithTags("Agent")
.RequireAuthorization()
.Produces<AgentAskResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/api/agent/pending", (PendingApprovalStore store) =>
    Results.Ok(store.ListPending().Select(p => new PendingApprovalDto
    {
        Id = p.Id,
        Tool = p.Tool,
        RequiresApproval = p.RequiresApproval,
        Title = p.Title,
        Body = p.Body,
        Severity = p.Severity,
        RequestedBy = p.RequestedBy,
        CreatedUtc = p.CreatedUtc,
        Status = p.Status
    })))
.WithName("ListPendingApprovals")
.WithTags("Agent")
.RequireAuthorization("SupervisorOrAdmin");

app.MapPost("/api/agent/approve/{id:guid}", (
    Guid id,
    AgentAskHandler agent,
    TicketStore tickets,
    HttpContext http) =>
{
    var roles = UserRoles.From(http.User);
    if (!roles.Any(r => r is "Supervisor" or "Admin"))
    {
        return Results.Forbid();
    }

    var username = http.User.Identity?.Name
        ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    var ticket = agent.Approve(id, username, roles, tickets);
    if (ticket is null)
    {
        return Results.NotFound(new { title = "Not found", detail = "No pending approval with that id." });
    }

    return Results.Ok(new
    {
        status = "created",
        ticketId = ticket.Id,
        title = ticket.Title,
        severity = ticket.Severity,
        approvedBy = ticket.ApprovedBy,
        createdUtc = ticket.CreatedUtc
    });
})
.WithName("ApproveTicket")
.WithTags("Agent")
.RequireAuthorization("SupervisorOrAdmin");

app.MapPost("/api/agent/reject/{id:guid}", (
    Guid id,
    AgentAskHandler agent,
    HttpContext http) =>
{
    var roles = UserRoles.From(http.User);
    var username = http.User.Identity?.Name
        ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    if (!agent.Reject(id, username, roles))
    {
        return Results.NotFound(new { title = "Not found", detail = "No pending approval with that id (or not allowed)." });
    }

    return Results.Ok(new { status = "rejected", id });
})
.WithName("RejectTicket")
.WithTags("Agent")
.RequireAuthorization("SupervisorOrAdmin");

app.MapGet("/api/tickets", (TicketStore tickets) => Results.Ok(tickets.List()))
.WithName("ListTickets")
.WithTags("Agent")
.RequireAuthorization("SupervisorOrAdmin");

app.Run();

public partial class Program;
