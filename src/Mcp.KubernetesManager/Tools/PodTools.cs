using k8s;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Mcp.KubernetesManager.Tools;

/// <summary>
/// MCP tools for managing Kubernetes pods.
/// These are the functions the AI can call directly.
/// </summary>
[McpServerToolType]
public class PodTools
{
    private readonly IKubernetes _client;

    public PodTools(IKubernetes client)
    {
        _client = client;
    }

    /// <summary>
    /// Lists all pods across all namespaces or a specific one.
    /// The AI calls this when asked "show me all running pods"
    /// </summary>
    [McpServerTool, Description("List all pods in the Kubernetes cluster. " +
        "Optionally filter by namespace. Returns pod name, namespace, status, and age.")]
    public async Task<string> ListPods(
        [Description("Kubernetes namespace to filter by. Leave empty for all namespaces.")]
        string? namespaceName = null)
    {
        var sb = new StringBuilder();

        try
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                // Get pods from all namespaces
                var pods = await _client.CoreV1.ListPodForAllNamespacesAsync();

                sb.AppendLine($"Found {pods.Items.Count} pods across all namespaces:\n");

                foreach (var pod in pods.Items.OrderBy(p => p.Metadata.NamespaceProperty))
                {
                    var status    = pod.Status.Phase ?? "Unknown";
                    var age       = DateTime.UtcNow - pod.Metadata.CreationTimestamp;
                    var ageString = age?.TotalHours >= 24
                        ? $"{(int)(age?.TotalDays ?? 0)}d"
                        : $"{(int)(age?.TotalHours ?? 0)}h";

                    sb.AppendLine($"  {pod.Metadata.NamespaceProperty}/{pod.Metadata.Name}");
                    sb.AppendLine($"    Status: {status} | Age: {ageString}");
                }
            }
            else
            {
                // Get pods from specific namespace
                var pods = await _client.CoreV1
                    .ListNamespacedPodAsync(namespaceName);

                sb.AppendLine($"Found {pods.Items.Count} pods in namespace '{namespaceName}':\n");

                foreach (var pod in pods.Items)
                {
                    var status    = pod.Status.Phase ?? "Unknown";
                    var ready     = pod.Status.ContainerStatuses?
                        .All(c => c.Ready) == true ? "Ready" : "Not Ready";
                    var restarts  = pod.Status.ContainerStatuses?
                        .Sum(c => c.RestartCount) ?? 0;

                    sb.AppendLine($"  {pod.Metadata.Name}");
                    sb.AppendLine($"    Status: {status} | {ready} | Restarts: {restarts}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error listing pods: {ex.Message}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets logs from a specific pod.
    /// The AI calls this when asked "show me logs from the idp-platform pod"
    /// </summary>
    [McpServerTool, Description("Get logs from a specific pod. " +
        "Useful for debugging issues or checking application output.")]
    public async Task<string> GetPodLogs(
        [Description("Name of the pod to get logs from.")]
        string podName,
        [Description("Namespace the pod is in. Defaults to 'default'.")]
        string namespaceName = "default",
        [Description("Number of recent log lines to return. Defaults to 50.")]
        int tailLines = 50)
    {
        try
        {
            // ReadNamespacedPodLogAsync returns a Stream — we need to read it
            var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
                name:               podName,
                namespaceParameter: namespaceName,
                tailLines:          tailLines);

            // Read the stream into a string
            using var reader = new StreamReader(stream);
            var logs = await reader.ReadToEndAsync();

            return string.IsNullOrWhiteSpace(logs)
                ? $"No logs found for pod '{podName}' in namespace '{namespaceName}'"
                : $"Last {tailLines} lines from {podName}:\n\n{logs}";
        }
        catch (Exception ex)
        {
            return $"Error getting logs for pod '{podName}': {ex.Message}";
        }
    }
}