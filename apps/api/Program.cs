using System.Text.Json.Serialization;
using Asp.Versioning;
using Api.Advisers.Queries;
using Api.Advisers.Handlers;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Controllers;
using Api.Persistence;
using Api.Quotes.Commands;
using Api.Quotes.Queries;
using Api.Quotes.Handlers;
using Api.Quotes.Services;
using Api.Telemetry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// One JSON object per line; the scopes carry the current TraceId and SpanId (the host turns activity tracking on).
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// The connection string is read when a DbContext is first built, so a host that swaps the DbContext options never needs one.
string ConnectionString() => builder.Configuration.GetConnectionString("Quotes")
    ?? throw new InvalidOperationException("ConnectionStrings__Quotes is not set.");
builder.Services.AddDbContext<QuotesDbContext>(options => options.UseNpgsql(ConnectionString()).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<DemoDataSeeder>();
// Controllers serialise with the MVC options; the OpenAPI document reads the HTTP ones. Both need the enum converter.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ICommandHandler<SaveQuoteCommand, SaveQuoteResult>, SaveQuoteHandler>();
builder.Services.AddScoped<ICommandHandler<SendQuoteToClientCommand, QuoteStatusResult>, SendQuoteToClientHandler>();
builder.Services.AddScoped<ICommandHandler<AcceptQuoteCommand, QuoteStatusResult>, AcceptQuoteHandler>();
builder.Services.AddScoped<ICommandHandler<DeclineQuoteCommand, QuoteStatusResult>, DeclineQuoteHandler>();
builder.Services.AddScoped<IQueryHandler<GetQuoteQuery, QuoteDetails>, GetQuoteHandler>();
builder.Services.AddScoped<IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>>, ListQuotesHandler>();
builder.Services.AddScoped<IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>, ListAdvisersHandler>();
builder.Services.AddScoped<CurrentAdviser>();
builder.Services.AddScoped<ICurrentAdviser>(sp => sp.GetRequiredService<CurrentAdviser>());
builder.Services.AddScoped<IAdviserDirectory, EfAdviserDirectory>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTracing(builder.Configuration);
builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = ApiRoutes.V1;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi();
builder.Services.AddControllerOperationDescriptions();
builder.Services.AddHealthChecks().AddDbContextCheck<QuotesDbContext>("database");

var app = builder.Build();

app.UseTraceIdHeader();
// Kestrel skips OnStarting callbacks for an unhandled exception, so without a handler a 500 would have no X-Trace-Id.
// Every environment gets the same ProblemDetails 500; a malformed request body stays a 400.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = e => e is BadHttpRequestException b ? b.StatusCode : StatusCodes.Status500InternalServerError,
});

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<QuotesDbContext>().Database.MigrateAsync();
}

if (app.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
}

// Health stays unversioned: probes should not need to know the API version.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();
