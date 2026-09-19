import { z } from "zod";

export const projectManifestSchema = z.object({
  id: z.string().min(2),
  name: z.string().min(2),
  type: z.enum(["static-website", "dynamic-website", "saas", "erp-crm", "api", "pwa"]),
  repository: z.object({
    fullName: z.string().min(3),
    defaultBranch: z.string().default("main")
  }),
  environments: z.array(z.enum(["development", "staging", "production"])).min(1),
  productionProtected: z.boolean().default(true),
  healthPath: z.string().default("/api/health")
});

export type ProjectManifest = z.infer<typeof projectManifestSchema>;