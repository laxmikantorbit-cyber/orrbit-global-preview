import { randomUUID } from "node:crypto";

export type CostCategory = "ai" | "hosting" | "database" | "storage" | "network" | "other";

export type ProjectBudget = {
  id: string;
  projectId: string;
  currency: "INR" | "USD";
  monthlyLimit: number;
  warningPercent: number;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
};

export type CostLedgerEntry = {
  id: string;
  projectId: string;
  provider: string;
  category: CostCategory;
  amount: number;
  currency: "INR" | "USD";
  note: string;
  occurredAt: string;
  createdAt: string;
};

export type CostBudgetSummary = {
  budget: ProjectBudget | null;
  currentMonthSpend: number;
  warningAmount: number | null;
  remainingAmount: number | null;
  status: "no_budget" | "within_budget" | "warning" | "over_budget";
  entryCount: number;
  automationBlocked: boolean;
};

function money(value: number, field: string) {
  if (!Number.isFinite(value) || value < 0 || value > 100000000) throw new Error(`invalid_${field}`);
  return Math.round(value * 100) / 100;
}

export function createProjectBudget(input: {
  projectId: string;
  currency?: string;
  monthlyLimit?: number;
  warningPercent?: number;
}): ProjectBudget {
  if (!["INR","USD"].includes(input.currency ?? "")) throw new Error("invalid_budget_currency");
  const monthlyLimit = money(Number(input.monthlyLimit), "monthly_limit");
  if (monthlyLimit <= 0) throw new Error("monthly_limit_must_be_positive");
  const warningPercent = Number(input.warningPercent ?? 80);
  if (!Number.isInteger(warningPercent) || warningPercent < 1 || warningPercent > 100) throw new Error("warning_percent_1_to_100_required");
  const now = new Date().toISOString();
  return {
    id: randomUUID(), projectId: input.projectId,
    currency: input.currency as "INR" | "USD",
    monthlyLimit, warningPercent, enabled: true,
    createdAt: now, updatedAt: now
  };
}

export function createCostLedgerEntry(input: {
  projectId: string;
  provider?: string;
  category?: string;
  amount?: number;
  currency?: string;
  note?: string;
  occurredAt?: string;
}): CostLedgerEntry {
  if (!["ai","hosting","database","storage","network","other"].includes(input.category ?? "")) throw new Error("invalid_cost_category");
  if (!["INR","USD"].includes(input.currency ?? "")) throw new Error("invalid_cost_currency");
  const provider = input.provider?.trim() ?? "";
  if (!provider || provider.length > 100) throw new Error("invalid_cost_provider");
  const note = input.note?.trim() ?? "";
  if (note.length > 300) throw new Error("cost_note_too_long");
  const occurred = input.occurredAt ? new Date(input.occurredAt) : new Date();
  if (Number.isNaN(occurred.getTime())) throw new Error("invalid_cost_date");
  return {
    id: randomUUID(), projectId: input.projectId, provider,
    category: input.category as CostCategory,
    amount: money(Number(input.amount), "cost_amount"),
    currency: input.currency as "INR" | "USD",
    note, occurredAt: occurred.toISOString(), createdAt: new Date().toISOString()
  };
}

export function summarizeCostBudget(budget: ProjectBudget | null, entries: CostLedgerEntry[], now = new Date()): CostBudgetSummary {
  const current = entries.filter((entry) => {
    const d = new Date(entry.occurredAt);
    return d.getUTCFullYear() === now.getUTCFullYear() && d.getUTCMonth() === now.getUTCMonth() &&
      (!budget || entry.currency === budget.currency);
  });
  const spend = Math.round(current.reduce((sum, entry) => sum + entry.amount, 0) * 100) / 100;
  if (!budget || !budget.enabled) {
    return { budget: budget ?? null, currentMonthSpend: spend, warningAmount: null, remainingAmount: null, status: "no_budget", entryCount: current.length, automationBlocked: false };
  }
  const warningAmount = Math.round(budget.monthlyLimit * budget.warningPercent) / 100;
  const remainingAmount = Math.round((budget.monthlyLimit - spend) * 100) / 100;
  const status = spend > budget.monthlyLimit ? "over_budget" : spend >= warningAmount ? "warning" : "within_budget";
  return { budget, currentMonthSpend: spend, warningAmount, remainingAmount, status, entryCount: current.length, automationBlocked: status === "over_budget" };
}
