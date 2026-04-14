using k8s;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Mcp.KubernetesManager.Tools;

/// <summary>
/// MCP tools for listing and inspecting Kubernetes namespaces.
/// </summary>
[McpServerToolType]
public class NamespaceTools
{
    private readonly IKubernetes _client;

    public NamespaceTools(IKubernetes client)
    {
        _client = client;
    }

    /// <summary>
    /// Lists all namespaces in the cluster with their status.
    /// The AI calls this when asked "what namespaces do I have?"
    /// </summary>
    [McpServerTool, Description("List all Kubernetes namespaces in the cluster. " +
        "Shows namespace name, status, and age.")]
    public async Task<string> ListNamespaces()
    {
        var sb = new StringBuilder();

        try
        {
            var namespaces = await _client.CoreV1.ListNamespaceAsync();

            sb.AppendLine($"Found {namespaces.Items.Count} namespaces:\n");

            foreach (var ns in namespaces.Items.OrderBy(n => n.Metadata.Name))
            {
                var status = ns.Status.Phase ?? "Unknown";
                var age    = DateTime.UtcNow - ns.Metadata.CreationTimestamp;
                var ageStr = age?.TotalDays >= 1
                    ? $"{(int)(age?.TotalDays ?? 0)}d"
                    : $"{(int)(age?.TotalHours ?? 0)}h";

                sb.AppendLine($"  {ns.Metadata.Name}");
                sb.AppendLine($"    Status: {status} | Age: {ageStr}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error listing namespaces: {ex.Message}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a summary of everything running in a namespace.
    /// The AI calls this when asked "what's running in idp-platform?"
    /// </summary>
    [McpServerTool, Description("Get a full summary of all resources running in a namespace. " +
        "Shows pods, deployments, and services in one view.")]
    public async Task<string> GetNamespaceSummary(
        [Description("The namespace to summarise.")]
        string namespaceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Summary for namespace '{namespaceName}':\n");

        try
        {
            // Pods
            var pods = await _client.CoreV1
                .ListNamespacedPodAsync(namespaceName);
            sb.AppendLine($"Pods ({pods.Items.Count}):");
            foreach (var pod in pods.Items)
                sb.AppendLine($"  {pod.Metadata.Name} — {pod.Status.Phase}");

            // Deployments
            var deployments = await _client.AppsV1
                .ListNamespacedDeploymentAsync(namespaceName);
            sb.AppendLine($"\nDeployments ({deployments.Items.Count}):");
            foreach (var d in deployments.Items)
                sb.AppendLine($"  {d.Metadata.Name} — " +
                    $"{d.Status.ReadyReplicas ?? 0}/{d.Spec.Replicas} ready");

            // Services
            var services = await _client.CoreV1
                .ListNamespacedServiceAsync(namespaceName);
            sb.AppendLine($"\nServices ({services.Items.Count}):");
            foreach (var svc in services.Items)
                sb.AppendLine($"  {svc.Metadata.Name} — {svc.Spec.Type}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error getting namespace summary: {ex.Message}");
        }

        return sb.ToString();
    }
}