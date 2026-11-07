using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Deltanium.Blocker;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Data;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Utils;
using Deltanium.Blocker.Middleware;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// --- Serilog Configuration (START) ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(Directory.GetCurrentDirectory(), "logs", "blocker.log"), // Serilog will append date automatically
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Blocker Web Host");

    // Load configuration
    var config = NodeConfig.LoadDefaults();

    var builder = WebApplication.CreateBuilder(args);
    var centralApiUrl = builder.Configuration["CentralApiUrl"] ?? Environment.GetEnvironmentVariable("CENTRAL_API_URL");
    if (!string.IsNullOrWhiteSpace(centralApiUrl))
    {
        var uri = Uri.TryCreate(centralApiUrl.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u : null;
        if (uri != null)
        {
            config = new NodeConfig
            {
                NodePublicKey = config.NodePublicKey,
                NodePrivateKey = config.NodePrivateKey,
                CentralEndpoint = uri,
                CentralApiPublicKey = config.CentralApiPublicKey,
                NodeEndpoint = config.NodeEndpoint,
                BlockCreationInterval = config.BlockCreationInterval,
                ValidationPollInterval = config.ValidationPollInterval,
                BlockVersion = config.BlockVersion
            };
        }
    }
    var blockVersion = builder.Configuration["BlockVersion"];
    if (!string.IsNullOrWhiteSpace(blockVersion))
    {
        config = new NodeConfig
        {
            NodePublicKey = config.NodePublicKey,
            NodePrivateKey = config.NodePrivateKey,
            CentralEndpoint = config.CentralEndpoint,
            CentralApiPublicKey = config.CentralApiPublicKey,
            NodeEndpoint = config.NodeEndpoint,
            BlockCreationInterval = config.BlockCreationInterval,
            ValidationPollInterval = config.ValidationPollInterval,
            BlockVersion = blockVersion.Trim()
        };
    }

    var apiUrl = config.CentralEndpoint.ToString().TrimEnd('/');

    // Optionally fetch Central API public key if not set (for verifying decision and block-slot signatures)
    if (string.IsNullOrWhiteSpace(config.CentralApiPublicKey))
    {
        var apiUrlForFetch = apiUrl;
        try
        {
            using var http = new HttpClient();
            var keyResp = await http.GetAsync($"{apiUrlForFetch.TrimEnd('/')}/api/blockcreatornode/decision-key");
            if (keyResp.IsSuccessStatusCode)
            {
                var keyJson = await keyResp.Content.ReadAsStringAsync();
                var keyDoc = System.Text.Json.JsonDocument.Parse(keyJson);
                var pk = keyDoc.RootElement.GetProperty("publicKey").GetString();
                if (!string.IsNullOrWhiteSpace(pk))
                {
                    config = new NodeConfig
                    {
                        NodePublicKey = config.NodePublicKey,
                        NodePrivateKey = config.NodePrivateKey,
                        CentralEndpoint = config.CentralEndpoint,
                        CentralApiPublicKey = pk.Trim(),
                        NodeEndpoint = config.NodeEndpoint,
                        BlockCreationInterval = config.BlockCreationInterval,
                        ValidationPollInterval = config.ValidationPollInterval,
                        BlockVersion = config.BlockVersion
                    };
                    Log.Information("Fetched Central API public key from decision-key endpoint.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not fetch Central API public key; will use key from decision response if not set.");
        }
    }

    // Register node with Central API if needed
    var nodeEndpoint = Environment.GetEnvironmentVariable("NODE_ENDPOINT") ?? config.NodeEndpoint;
    builder.WebHost.UseUrls(nodeEndpoint); // Configure listening URL
    builder.Host.UseSerilog();

    var voucher = Environment.GetEnvironmentVariable("VOUCHER")
        ?? builder.Configuration["NodeRegistration:Voucher"]
        ?? builder.Configuration["VOUCHER"];
    if (string.IsNullOrWhiteSpace(voucher))
        throw new InvalidOperationException(
            "Node registration voucher required. Set VOUCHER or NodeRegistration:Voucher (user-secrets).");

    // Register ILogger
    builder.Services.AddSingleton(Log.Logger);
    builder.Services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

    builder.Services.AddSingleton(config);

    // Register NodeRegistrationService and perform initial registration
    builder.Services.AddSingleton<NodeRegistrationService>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<NodeRegistrationService>>();
        return new NodeRegistrationService(apiUrl, nodeEndpoint, voucher, logger);
    });

    using var loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder.AddSerilog());
    var registrationService = new NodeRegistrationService(apiUrl, nodeEndpoint, voucher, loggerFactory.CreateLogger<NodeRegistrationService>());
    var registrationResult = await registrationService.RegisterNodeIfNeededAsync();
    if (!string.IsNullOrWhiteSpace(config.CentralApiPublicKey) && File.Exists("node_info.json"))
        await registrationService.SaveCentralApiPublicKeyAsync(config.CentralApiPublicKey);
    var useLocalDecision = string.Equals(Environment.GetEnvironmentVariable("USE_LOCAL_DECISION"), "true", StringComparison.OrdinalIgnoreCase);

    if (!registrationResult.Success)
    {
        if (useLocalDecision)
        {
            Log.Warning("Node registration failed (Central API unavailable). USE_LOCAL_DECISION=true, continuing for local dev.");
        }
        else
        {
            Log.Error($"Node registration failed: {registrationResult.ErrorMessage}");
            Environment.Exit(1);
        }
    }

    if (!registrationResult.AlreadyRegistered)
    {
        config = NodeConfig.LoadDefaults();
        builder.Services.AddSingleton(config);
    }

    if (config.NodePublicKey == "NODE_PUBKEY_PLACEHOLDER" || config.NodePrivateKey == "NODE_PRIVKEY_PLACEHOLDER")
    {
        Log.Error("Node keys not found. Please ensure node_info.json exists or registration completed successfully.");
        Environment.Exit(1);
    }

    Log.Information($"Node Public Key: {config.NodePublicKey.Substring(0, Math.Min(20, config.NodePublicKey.Length))}...");
    Log.Information($"Central API: {config.CentralEndpoint}");
    Log.Information($"Node Endpoint: {nodeEndpoint}");
    if (useLocalDecision)
        Log.Information("USE_LOCAL_DECISION=true: accepting transactions without Central Decision API.");

    var storageProvider = builder.Configuration["Storage:Provider"] ?? "Json";
    var connectionString = builder.Configuration.GetConnectionString("Default");

    if (string.Equals(storageProvider, "MySql", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "MySQL enabled but ConnectionStrings:Default is empty. " +
                "Set via User Secrets (id: deltanium-blocker-local-secrets).");
        }

        var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));
        builder.Services.AddDbContextFactory<BlockerDbContext>(options =>
            options.UseMySql(connectionString, serverVersion));
        builder.Services.AddSingleton<IBlockStore, MySqlBlockStore>();
        builder.Services.AddSingleton<IMempoolStore, MySqlMempoolStore>();
        Log.Information("Blocker Storage:Provider=MySql");
    }
    else
    {
        builder.Services.AddSingleton<IBlockStore>(_ => new JsonBlockStore("blocks.json"));
        builder.Services.AddSingleton<IMempoolStore>(_ => new JsonMempoolStore("mempool.json"));
        Log.Information("Blocker Storage:Provider=Json");
    }

    // Register other services
    builder.Services.AddSingleton<TransactionPool>(provider =>
        new TransactionPool(provider.GetRequiredService<IMempoolStore>(), provider.GetRequiredService<ILogger<TransactionPool>>()));
    builder.Services.AddSingleton<DecisionClient>(provider =>
        new DecisionClient(apiUrl, provider.GetRequiredService<ILogger<DecisionClient>>()));
    builder.Services.AddSingleton<NodeState>(provider =>
        new NodeState(provider.GetRequiredService<IBlockStore>()));
    builder.Services.AddSingleton<CentralCoordinatorClient>(provider =>
        new CentralCoordinatorClient(config, provider.GetRequiredService<ILogger<CentralCoordinatorClient>>()));
    builder.Services.AddSingleton<BlockValidator>(provider =>
        new BlockValidator(config, provider.GetRequiredService<ILogger<BlockValidator>>()));
    builder.Services.AddSingleton<BlockBuilder>(provider =>
        new BlockBuilder(config, provider.GetRequiredService<TransactionPool>(), provider.GetRequiredService<NodeState>(), provider.GetRequiredService<ILogger<BlockBuilder>>()));
    builder.Services.AddSingleton<TransactionValidationService>(provider =>
        new TransactionValidationService(provider.GetRequiredService<ILogger<TransactionValidationService>>(), config.CentralApiPublicKey));
    builder.Services.AddSingleton<MetricsService>();
    builder.Services.AddSingleton<NodeHost>();

    Log.Information("Starting Deltanium Block Creator node (MVP)...");

    // Add services to the container.
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    var app = builder.Build();

    if (string.Equals(storageProvider, "MySql", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BlockerDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    // Configure the HTTP request pipeline.
    // if (app.Environment.IsDevelopment())
    // {
    //    // Swagger removed
    // }

    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseAuthorization();
    app.MapControllers();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapGet("/metrics", (MetricsService metrics) => Results.Ok(metrics.Snapshot()));

    // Start NodeHost in a background task
    _ = Task.Run(() => app.Services.GetRequiredService<NodeHost>().RunAsync());

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
