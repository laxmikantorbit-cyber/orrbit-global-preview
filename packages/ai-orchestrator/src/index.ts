import { randomUUID } from "node:crypto";

export interface ProvisioningPlan {
  id: string;
  planner: "local-development" | "openai";
  prompt: string;
  suggestedName: string;
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

function inferProjectName(prompt: string): string {
  const cleaned = prompt.replace(/\s+/g, " ").trim();
  const titled = cleaned.match(/\b([A-Z][A-Za-z0-9&™.-]*(?:\s+[A-Z][A-Za-z0-9&™.-]*){0,6}\s+(?:ERP|CRM|SaaS|Platform|Portal|Website|App))\b/);
  if (titled?.[1]) return titled[1].trim();
  const match = cleaned.match(/(?:my|the|import|create|add)\s+([A-Z][A-Za-z0-9&™ .-]{2,60}?)(?:\s+(?:from|as|to|project|and)\b|[,.]|$)/i);
  if (match?.[1]) return match[1].trim().replace(/^(my|the)\s+/i, "");
  return "AI Planned Project";
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
    id: randomUUID(), planner: "local-development", prompt,
    suggestedName: inferProjectName(prompt),
    inferredProjectType, sourceMode, targetEnvironment, risk,
    actions: ["Create project draft", "Prepare source workspace", "Prepare environment plan", "Run validation before any apply"],
    requiresApproval: true, executionAllowed: false
  };
}
