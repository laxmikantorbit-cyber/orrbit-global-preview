import { randomUUID } from "node:crypto";

export interface ProvisioningPlan {
  id: string;
  planner: "local-development" | "openai";
  prompt: string;
  inferredProjectType: "static-website" | "dynamic-website" | "saas" | "erp-crm" | "api" | "pwa";
  sourceMode: "new-project" | "existing-repository" | "import";
  targetEnvironment: "development" | "staging" | "production";
  risk: "low" | "medium" | "high" | "critical";
  actions: string[];
  requiresApproval: true;
  executionAllowed: false;
}

function productionIsExplicitlyDisabled(text: string): boolean {
  return /production\s+(disabled|off|locked|later)/.test(text)
    || /(do not|don't|not)\s+.*production/.test(text);
}

export function createLocalProvisioningPlan(prompt: string): ProvisioningPlan {
  const text = prompt.toLowerCase();
  const inferredProjectType = text.includes("erp") ? "erp-crm"
    : text.includes("saas") ? "saas"
    : text.includes("pwa") ? "pwa"
    : text.includes("api") ? "api"
    : text.includes("dynamic") ? "dynamic-website" : "static-website";
  const sourceMode = text.includes("import") || text.includes("chatgpt sites") ? "import"
    : text.includes("existing repo") || text.includes("github") ? "existing-repository" : "new-project";
  const wantsProduction = text.includes("production") && !productionIsExplicitlyDisabled(text);
  const targetEnvironment = wantsProduction ? "production"
    : text.includes("staging") ? "staging" : "development";
  const risk = targetEnvironment === "production" || text.includes("dns") || text.includes("payment") ? "high" : "medium";
  return {
    id: randomUUID(), planner: "local-development", prompt, inferredProjectType, sourceMode,
    targetEnvironment, risk,
    actions: ["Create project draft", "Prepare source workspace", "Prepare environment plan", "Run validation before any apply"],
    requiresApproval: true, executionAllowed: false
  };
}