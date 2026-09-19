export type ProviderMode = "dry-run" | "connected";
export type ProviderRisk = "low" | "medium" | "high" | "critical";

export interface ProviderActionPlan {
  provider: string;
  action: string;
  mode: ProviderMode;
  risk: ProviderRisk;
  requiresApproval: boolean;
  executionAllowed: boolean;
  notes: string[];
}

export interface DeploymentResult {
  providerDeploymentId: string;
  url?: string;
  revision?: string;
  evidence: string[];
}

export interface FrontendProvider {
  name: string;
  createPreview(projectId: string, ref: string): Promise<DeploymentResult>;
  deployProduction(projectId: string, ref: string): Promise<DeploymentResult>;
  rollback(projectId: string, deploymentId: string): Promise<DeploymentResult>;
}

export interface BackendProvider {
  name: string;
  deploy(projectId: string, imageRef: string): Promise<DeploymentResult>;
  rollback(projectId: string, revision: string): Promise<DeploymentResult>;
  health(projectId: string): Promise<boolean>;
}

export interface SourceControlProvider {
  name: string;
  createFeatureBranch(projectId: string, baseBranch: string, branchName: string): Promise<ProviderActionPlan>;
  openChangeRequest(projectId: string, branchName: string): Promise<ProviderActionPlan>;
}

export interface DatabaseProvider {
  name: string;
  planMigration(projectId: string, environment: string, summary: string): Promise<ProviderActionPlan>;
  backup(projectId: string, environment: string): Promise<ProviderActionPlan>;
}

export interface AiProvider {
  name: string;
  planCodeChange(projectId: string, prompt: string): Promise<ProviderActionPlan>;
}

export interface SecretProvider {
  name: string;
  referenceSecret(projectId: string, secretName: string): Promise<ProviderActionPlan>;
}

function dryRun(provider: string, action: string, risk: ProviderRisk, notes: string[]): ProviderActionPlan {
  return { provider, action, mode: "dry-run", risk, requiresApproval: risk === "high" || risk === "critical", executionAllowed: false, notes };
}

export const providerCapabilities = {
  sourceControl: ["github"],
  frontend: ["cloudflare-pages"],
  backend: ["google-cloud-run"],
  database: ["postgres", "mongodb"],
  storage: ["none", "cloudflare-r2"],
  ai: ["openai"],
  secret: ["google-secret-manager"]
} as const;

export class DryRunGitHubProvider implements SourceControlProvider {
  readonly name = "github";
  async createFeatureBranch(projectId: string, baseBranch: string, branchName: string) {
    return dryRun(this.name, "create_feature_branch", "medium", [projectId, baseBranch, branchName, "GitHub App connection required before execution"]);
  }
  async openChangeRequest(projectId: string, branchName: string) {
    return dryRun(this.name, "open_change_request", "medium", [projectId, branchName, "Creates reviewable change request only after provider connection"]);
  }
}

export class DryRunCloudflarePagesProvider implements FrontendProvider {
  readonly name = "cloudflare-pages";
  async createPreview(projectId: string, ref: string): Promise<DeploymentResult> {
    return { providerDeploymentId: `dryrun-preview-${projectId}`, url: `https://preview.invalid/${ref}`, evidence: ["dry_run_only", "no_cloudflare_call"] };
  }
  async deployProduction(projectId: string, ref: string): Promise<DeploymentResult> {
    return { providerDeploymentId: `locked-production-${projectId}`, revision: ref, evidence: ["production_locked", "owner_approval_required"] };
  }
  async rollback(projectId: string, deploymentId: string): Promise<DeploymentResult> {
    return { providerDeploymentId: deploymentId, evidence: ["rollback_plan_only", "provider_connection_required"] };
  }
}

export class DryRunCloudRunProvider implements BackendProvider {
  readonly name = "google-cloud-run";
  async deploy(projectId: string, imageRef: string): Promise<DeploymentResult> {
    return { providerDeploymentId: `dryrun-cloudrun-${projectId}`, revision: imageRef, evidence: ["dry_run_only", "no_google_cloud_call"] };
  }
  async rollback(projectId: string, revision: string): Promise<DeploymentResult> {
    return { providerDeploymentId: `dryrun-rollback-${projectId}`, revision, evidence: ["rollback_plan_only"] };
  }
  async health(): Promise<boolean> { return true; }
}

export class DryRunDatabaseProvider implements DatabaseProvider {
  readonly name = "database";
  async planMigration(projectId: string, environment: string, summary: string) {
    const risk: ProviderRisk = environment === "production" ? "high" : "medium";
    return dryRun(this.name, "plan_database_migration", risk, [projectId, environment, summary, "Migration preview only"]);
  }
  async backup(projectId: string, environment: string) {
    const risk: ProviderRisk = environment === "production" ? "high" : "medium";
    return dryRun(this.name, "backup_database", risk, [projectId, environment, "Backup provider required before execution"]);
  }
}

export class DryRunOpenAiProvider implements AiProvider {
  readonly name = "openai";
  async planCodeChange(projectId: string, prompt: string) {
    return dryRun(this.name, "plan_code_change", "medium", [projectId, prompt.slice(0, 120), "Model routing not connected yet"]);
  }
}

export class DryRunSecretProvider implements SecretProvider {
  readonly name = "google-secret-manager";
  async referenceSecret(projectId: string, secretName: string) {
    return dryRun(this.name, "reference_secret", "high", [projectId, secretName, "Secret value is never exposed to AI"]);
  }
}
