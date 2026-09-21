import { randomUUID } from "node:crypto";
import type { ProviderActionPlan, SecretProvider } from "@orrbit/provider-adapters";

export type SecretReferenceEnvironment = "development" | "staging" | "production";

export type SecretReferenceRecord = {
  id: string;
  projectId: string;
  environment: SecretReferenceEnvironment;
  secretName: string;
  provider: string;
  providerReference: string;
  providerPlan: ProviderActionPlan;
  status: "referenced";
  secretValueStored: false;
  createdAt: string;
  updatedAt: string;
};

function validSecretName(value: string) {
  const normalized = value.trim();
  if (!/^[A-Za-z][A-Za-z0-9_.-]{1,127}$/.test(normalized)) {
    throw new Error("invalid_secret_reference_name");
  }
  return normalized;
}

export function rejectSecretValueFields(body: Record<string, unknown>) {
  const forbidden = ["value", "secretValue", "plaintext", "password", "token", "apiKey", "privateKey"];
  const found = forbidden.find((key) => Object.prototype.hasOwnProperty.call(body, key));
  if (found) throw new Error("secret_values_must_not_enter_control_plane");
}

export async function createSecretReference(input: {
  projectId: string;
  environment?: string;
  secretName?: string;
  providerReference?: string;
}, provider: SecretProvider): Promise<SecretReferenceRecord> {
  const environment = input.environment;
  if (!["development", "staging", "production"].includes(environment ?? "")) {
    throw new Error("invalid_secret_environment");
  }
  const secretName = validSecretName(input.secretName ?? "");
  const providerReference = input.providerReference?.trim() ||
    `secretref://${provider.name}/${input.projectId}/${environment}/${encodeURIComponent(secretName)}`;
  if (providerReference.length > 500) throw new Error("secret_reference_too_long");
  const providerPlan = await provider.referenceSecret(input.projectId, secretName);
  const now = new Date().toISOString();
  return {
    id: randomUUID(),
    projectId: input.projectId,
    environment: environment as SecretReferenceEnvironment,
    secretName,
    provider: provider.name,
    providerReference,
    providerPlan,
    status: "referenced",
    secretValueStored: false,
    createdAt: now,
    updatedAt: now
  };
}
