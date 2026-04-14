using k8s;
using k8s.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Mcp.KubernetesManager.Tools;

/// <summary>
/// MCP tools for managing Kubernetes deployments.
/// These are the functions the AI can call directly.
/// </summary>
[McpServerToolType]
public class DeploymentTools
{
    private readonly IKubernetes _client;

    public DeploymentTools(IKubernetes client)
    {
        _client = client;
    }

    /// <summary>
    /// Lists all deployments across all namespaces or a specific one.
    /// The AI calls this when asked "what's the status of my deployments?"
    /// </summary>
    [McpServerTool, Description("List all deployments in the Kubernetes cluster. " +
        "Shows deployment name, namespace, desired vs ready replicas, and status.")]
    public async Task<string> ListDeployments(
        [Description("Kubernetes namespace to filter by. Leave empty for all namespaces.")]
        string? namespaceName = null)
    {
        var sb = new StringBuilder();

        try
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                var deployments = await _client.AppsV1
                    .ListDeploymentForAllNamespacesAsync();

                sb.AppendLine($"Found {deployments.Items.Count} deployments:\n");

                foreach (var d in deployments.Items
                    .OrderBy(d => d.Metadata.NamespaceProperty))
                {
                    var desired = d.Spec.Replicas ?? 0;
                    var ready   = d.Status.ReadyReplicas ?? 0;
                    var status  = ready == desired ? "Healthy" : "Degraded";

                    sb.AppendLine($"  {d.Metadata.NamespaceProperty}/{d.Metadata.Name}");
                    sb.AppendLine($"    Replicas: {ready}/{desired} | Status: {status}");
                }
            }
            else
            {
                var deployments = await _client.AppsV1
                    .ListNamespacedDeploymentAsync(namespaceName);

                sb.AppendLine($"Deployments in '{namespaceName}':\n");

                foreach (var d in deployments.Items)
                {
                    var desired = d.Spec.Replicas ?? 0;
                    var ready   = d.Status.ReadyReplicas ?? 0;
                    var status  = ready == desired ? "Healthy" : "Degraded";

                    sb.AppendLine($"  {d.Metadata.Name}");
                    sb.AppendLine($"    Replicas: {ready}/{desired} | Status: {status}");
                    sb.AppendLine($"    Image: {d.Spec.Template.Spec.Containers[0].Image}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error listing deployments: {ex.Message}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Scales a deployment to a specified number of replicas.
    /// The AI calls this when asked "scale idp-platform to 3 replicas"
    /// </summary>
    [McpServerTool, Description("Scale a Kubernetes deployment to a specified number of replicas. " +
        "Use this to scale up for more traffic or scale down to save resources.")]
    public async Task<string> ScaleDeployment(
        [Description("Name of the deployment to scale.")]
        string deploymentName,
        [Description("Number of replicas to scale to. Use 0 to stop the deployment.")]
        int replicas,
        [Description("Namespace the deployment is in. Defaults to 'default'.")]
        string namespaceName = "default")
    {
        // Safety check — prevent accidentally scaling too high
        if (replicas > 10)
            return $"Scaling to {replicas} replicas is not allowed. Maximum is 10.";

        if (replicas < 0)
            return "Replicas cannot be negative.";

        try
        {
            // Get the current deployment
            var deployment = await _client.AppsV1
                .ReadNamespacedDeploymentAsync(deploymentName, namespaceName);

            var previousReplicas = deployment.Spec.Replicas ?? 0;

            // Update the replica count
            deployment.Spec.Replicas = replicas;

            await _client.AppsV1.ReplaceNamespacedDeploymentAsync(
                deployment, deploymentName, namespaceName);

            return $"Successfully scaled '{deploymentName}' in namespace '{namespaceName}' " +
                   $"from {previousReplicas} to {replicas} replicas.";
        }
        catch (Exception ex)
        {
            return $"Error scaling deployment '{deploymentName}': {ex.Message}";
        }
    }

    /// <summary>
    /// Restarts a deployment by updating an annotation.
    /// The AI calls this when asked "restart the idp-platform deployment"
    /// </summary>
    [McpServerTool, Description("Restart a Kubernetes deployment by triggering a rolling restart. " +
        "This is equivalent to running kubectl rollout restart.")]
    public async Task<string> RestartDeployment(
        [Description("Name of the deployment to restart.")]
        string deploymentName,
        [Description("Namespace the deployment is in. Defaults to 'default'.")]
        string namespaceName = "default")
    {
        try
        {
            var deployment = await _client.AppsV1
                .ReadNamespacedDeploymentAsync(deploymentName, namespaceName);

            // Adding/updating this annotation triggers a rolling restart
            // This is exactly what kubectl rollout restart does under the hood
            deployment.Spec.Template.Metadata.Annotations ??= new Dictionary<string, string>();
            deployment.Spec.Template.Metadata.Annotations["kubectl.kubernetes.io/restartedAt"]
                = DateTime.UtcNow.ToString("o");

            await _client.AppsV1.ReplaceNamespacedDeploymentAsync(
                deployment, deploymentName, namespaceName);

            return $"Successfully triggered rolling restart for '{deploymentName}' " +
                   $"in namespace '{namespaceName}'. " +
                   $"New pods are being created while old ones are gracefully terminated.";
        }
        catch (Exception ex)
        {
            return $"Error restarting deployment '{deploymentName}': {ex.Message}";
        }
    }

    /// <summary>
    /// Gets the rollout status of a deployment.
    /// The AI calls this when asked "is my deployment healthy?"
    /// </summary>
    [McpServerTool, Description("Get the detailed rollout status of a deployment. " +
        "Shows if a deployment is complete, progressing, or has failures.")]
    public async Task<string> GetDeploymentStatus(
        [Description("Name of the deployment to check.")]
        string deploymentName,
        [Description("Namespace the deployment is in. Defaults to 'default'.")]
        string namespaceName = "default")
    {
        try
        {
            var deployment = await _client.AppsV1
                .ReadNamespacedDeploymentAsync(deploymentName, namespaceName);

            var sb = new StringBuilder();
            sb.AppendLine($"Status for deployment '{deploymentName}':");
            sb.AppendLine($"  Namespace:  {namespaceName}");
            sb.AppendLine($"  Desired:    {deployment.Spec.Replicas} replicas");
            sb.AppendLine($"  Ready:      {deployment.Status.ReadyReplicas ?? 0} replicas");
            sb.AppendLine($"  Available:  {deployment.Status.AvailableReplicas ?? 0} replicas");
            sb.AppendLine($"  Updated:    {deployment.Status.UpdatedReplicas ?? 0} replicas");
            sb.AppendLine($"  Image:      {deployment.Spec.Template.Spec.Containers[0].Image}");

            // Check conditions for human-readable status
            var conditions = deployment.Status.Conditions ?? [];
            foreach (var condition in conditions)
            {
                sb.AppendLine($"  {condition.Type}: {condition.Status} — {condition.Message}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting status for '{deploymentName}': {ex.Message}";
        }
    }
}