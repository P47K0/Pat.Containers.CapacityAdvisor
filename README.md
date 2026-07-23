# Capacity Advisor

Capacity Advisor is a lightweight AIOps proof of concept for Kubernetes and Azure-focused workloads. It analyzes a single target workload, reads its current runtime and configuration signals, and returns advice about CPU and memory sizing, including whether a proposed change still fits existing node capacity in AKS.

This version is intentionally small and demo-oriented. It focuses on one workload at a time so the core advisory loop stays easy to understand, fast to ship, and simple to extend later.

## Purpose

The project is designed to demonstrate a practical AIOps flow instead of trying to replace native autoscaling or become a full platform product on day one. The current scope is a fast proof of concept for one pod or workload, with emphasis on observability, reasoning, and actionable recommendations.

The current advisor can be used to:
- Inspect the current target workload configuration.
- Read current resource requests, limits, and observed metrics.
- Evaluate whether new CPU and memory settings are reasonable.
- For AKS, reason about whether a workload still fits on existing node capacity based on Kubernetes scheduling concepts around requests and limits.

## Current scope

This version is intentionally limited to a single analyzed workload at a time. That keeps the demo focused and avoids unnecessary complexity while still proving the architecture and reasoning loop.

Current characteristics:
- Single workload assessment flow.
- Platform-specific metric collection through adapters.
- API-key-protected assessment endpoint.
- External AI agent integration for explanation generation.
- AKS-focused extension path, while preserving a clean platform abstraction.

## How it works

At a high level, the advisor follows this flow:
1. Receive an assessment request.
2. Load the target platform configuration.
3. Collect current workload metrics and resource settings.
4. Compare current state with proposed CPU and memory values.
5. Generate advisory output with explanation and recommendation.

For Kubernetes workloads, resource requests influence scheduling decisions, while limits define the enforced runtime cap for CPU and memory. That distinction is important for the AKS advisory logic, especially when deciding whether a new configuration still fits on a node.

## Architecture

The project is intentionally structured around separation of concerns:
- **API layer** for protected endpoints.
- **Advisor core** for assessment and recommendation logic.
- **Platform collector** abstraction for ACA and AKS specific data collection.
- **AI explanation client** for LLM-based explanation text.

This separation makes it easier to keep the reasoning logic reusable while swapping the platform-specific collector underneath. It also keeps the current proof of concept small without blocking future expansion.

## Configuration

The advisor is configured through standard ASP.NET Core configuration sources such as `appsettings.json` and environment variables. Sensitive values such as API keys should be provided through Kubernetes Secrets or other secret stores instead of being committed to source control.

Typical settings include:
- Target platform.
- Azure subscription and resource identifiers.
- AKS cluster, namespace, workload, and container names.
- AI agent URL and API key.
- Assessment API key.
- Metric window length.

For AKS, current CPU and memory requests and limits do not need to be statically configured if they are retrieved from the Kubernetes workload specification at runtime, because Kubernetes stores container resource requests and limits in the pod template definition.

## Deployment

The project is suitable for temporary AKS demo deployments and short-lived experiments. A simple Kubernetes deployment model using `Deployment`, `Service`, `Ingress`, and `Secret` resources is enough for this proof of concept, and Traefik can expose the advisor through a standard Kubernetes Ingress resource.

This repository is optimized for practical demo delivery rather than full production hardening. For the current version, simplicity and speed are intentional design choices.

## Why one workload only

This first AKS version focuses on one workload so the proof of concept stays fast, understandable, and easy to demonstrate. That makes it easier to validate the advisory loop before expanding toward broader cluster-wide assessment.

The goal of this version is not maximum feature coverage. The goal is to demonstrate that metrics, workload configuration, and platform context can be combined into useful capacity guidance.

## Extension plans

Planned future improvements include:
- Support for checking multiple pods or all workloads in a namespace.
- Periodic background assessments, for example once or a few times per day.
- Webhook-triggered reassessments from Azure Monitor alerts via action groups and the common alert schema.
- Better AKS placement reasoning using live node capacity and workload request data.
- Expanded platform support and cleaner platform-specific collectors.
- Improved history, trend storage, and comparison over time.

Webhook support is not limited to AKS. Azure Monitor action groups and alert webhooks can also be used with Azure Container Apps alerts, which makes the trigger model reusable across both platforms.

## Roadmap direction

The likely next phase is a hybrid model:
- Periodic namespace or workload-list scans for baseline assessments.
- Alert-driven execution for urgent events such as throttling or high memory usage.
- Deeper AKS node-fit analysis for proposed request and limit changes.

That approach balances broad coverage with fast event-driven analysis, while keeping the platform-specific reasoning where it belongs.

## Notes

This project is a portfolio-style AIOps and architecture demo. It is intentionally pragmatic, lightweight, and built to show credible advisory workflows in a short timeframe rather than to act as a full replacement for native Kubernetes scaling controls.
