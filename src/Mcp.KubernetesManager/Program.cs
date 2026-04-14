using k8s;
using Mcp.KubernetesManager.Tools;
using ModelContextProtocol.Server;
using Serilog;

// Set up structured logging to stderr
// MCP servers must write logs to stderr, not stdout
// stdout is reserved for the MCP protocol messages
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

try
{
    Log.Information("Starting Kubernetes MCP Server...");

    // Connect to the local Kubernetes cluster
    // This reads from ~/.kube/config automatically
    var k8sConfig = KubernetesClientConfiguration.IsInCluster()
        ? KubernetesClientConfiguration.InClusterConfig()
        : KubernetesClientConfiguration.BuildConfigFromConfigFile();

    var k8sClient = new Kubernetes(k8sConfig);

    Log.Information("Connected to Kubernetes cluster: {Host}", k8sConfig.Host);

    // Build the MCP server
    var builder = WebApplication.CreateBuilder();

    // Register Kubernetes client as a singleton
    // All tools share the same client instance
    builder.Services.AddSingleton<IKubernetes>(k8sClient);

    // Register all our tool classes
    builder.Services.AddSingleton<PodTools>();
    builder.Services.AddSingleton<DeploymentTools>();
    builder.Services.AddSingleton<NamespaceTools>();

    // Add MCP server with stdio transport
    // stdio = communicates via standard input/output
    // This is how Claude Desktop connects to MCP servers
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    Log.Information("MCP Server ready. Waiting for connections...");
    Log.Information("Available tools: ListPods, GetPodLogs, ListDeployments, " +
        "ScaleDeployment, RestartDeployment, GetDeploymentStatus, " +
        "ListNamespaces, GetNamespaceSummary");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCP Server failed to start");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;