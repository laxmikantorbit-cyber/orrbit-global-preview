import type { ActionContext, RiskLevel } from "@orrbit/contracts";

const highRisk = ["dns", "payment", "auth", "secret", "database"];
const critical = ["delete_project", "drop_database", "restore_production"];

export function classifyRisk(ctx: ActionContext): RiskLevel {
  if (ctx.destructive || critical.includes(ctx.action)) return "critical";
  if (ctx.environment === "production") return "high";
  if (highRisk.some((term) => ctx.action.includes(term))) return "high";
  if (ctx.action.includes("api") || ctx.action.includes("migration")) return "medium";
  return "low";
}

export function requiresApproval(ctx: ActionContext): boolean {
  return classifyRisk(ctx) === "high" || classifyRisk(ctx) === "critical";
}