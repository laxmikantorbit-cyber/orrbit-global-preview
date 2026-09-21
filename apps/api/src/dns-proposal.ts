import { randomUUID } from "node:crypto";

export type DnsRecordType = "A" | "AAAA" | "CNAME" | "TXT" | "MX" | "CAA";
export type DnsChangeAction = "create" | "update" | "delete";
export type DnsProposalStatus = "proposed" | "approved" | "cancelled";

export type DnsChangeProposal = {
  id: string;
  projectId: string;
  domain: string;
  action: DnsChangeAction;
  recordType: DnsRecordType;
  recordName: string;
  proposedValue: string | null;
  ttl: number;
  status: DnsProposalStatus;
  risk: "high";
  requiresApproval: true;
  restorePointRequired: true;
  executionAllowed: false;
  protections: {
    dnsExecutionLocked: true;
    productionLocked: true;
    liveTrafficProtected: true;
  };
  createdAt: string;
  updatedAt: string;
};

function validateDomain(value: string) {
  const domain = value.trim().toLowerCase();
  if (domain.length < 3 || domain.length > 253 || !/^(?=.{3,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/.test(domain)) {
    throw new Error("invalid_domain");
  }
  return domain;
}

function validateRecordName(value: string) {
  const name = value.trim();
  if (!name || name.length > 253 || /\s/.test(name)) throw new Error("invalid_dns_record_name");
  return name;
}

export function createDnsChangeProposal(input: {
  projectId: string;
  domain?: string;
  action?: string;
  recordType?: string;
  recordName?: string;
  proposedValue?: string | null;
  ttl?: number;
}): DnsChangeProposal {
  if (!["create", "update", "delete"].includes(input.action ?? "")) throw new Error("invalid_dns_action");
  if (!["A", "AAAA", "CNAME", "TXT", "MX", "CAA"].includes(input.recordType ?? "")) throw new Error("invalid_dns_record_type");
  const domain = validateDomain(input.domain ?? "");
  const recordName = validateRecordName(input.recordName ?? "");
  const ttl = Number(input.ttl ?? 300);
  if (!Number.isInteger(ttl) || ttl < 60 || ttl > 86400) throw new Error("dns_ttl_60_to_86400_required");
  const value = input.proposedValue?.trim() || null;
  if (input.action !== "delete" && !value) throw new Error("dns_value_required");
  if (value && value.length > 2048) throw new Error("dns_value_too_long");
  const now = new Date().toISOString();
  return {
    id: randomUUID(),
    projectId: input.projectId,
    domain,
    action: input.action as DnsChangeAction,
    recordType: input.recordType as DnsRecordType,
    recordName,
    proposedValue: value,
    ttl,
    status: "proposed",
    risk: "high",
    requiresApproval: true,
    restorePointRequired: true,
    executionAllowed: false,
    protections: { dnsExecutionLocked: true, productionLocked: true, liveTrafficProtected: true },
    createdAt: now,
    updatedAt: now
  };
}

export function approveDnsProposal(proposal: DnsChangeProposal) {
  if (proposal.status !== "proposed") throw new Error("dns_proposal_not_approvable");
  return { ...proposal, status: "approved" as const, executionAllowed: false as const, updatedAt: new Date().toISOString() };
}

export function cancelDnsProposal(proposal: DnsChangeProposal) {
  if (proposal.status === "cancelled") return proposal;
  return { ...proposal, status: "cancelled" as const, executionAllowed: false as const, updatedAt: new Date().toISOString() };
}
