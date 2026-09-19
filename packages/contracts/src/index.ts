export type ProjectType =
  | "static-website"
  | "dynamic-website"
  | "saas"
  | "erp-crm"
  | "api"
  | "pwa";

export type EnvironmentName = "development" | "staging" | "production";

export type RiskLevel = "low" | "medium" | "high" | "critical";

export type JobState =
  | "queued" | "analysing" | "plan_ready" | "awaiting_approval"
  | "executing" | "testing" | "preview_ready"
  | "awaiting_release_approval" | "deploying" | "verifying"
  | "succeeded" | "failed" | "blocked" | "cancelled" | "rolled_back";

export interface ActionContext {
  environment: EnvironmentName;
  action: string;
  destructive?: boolean;
}