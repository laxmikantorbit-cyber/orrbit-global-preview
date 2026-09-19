import { z } from "zod";

const repositorySchema = z.object({
  fullName: z.string().min(3),
  defaultBranch: z.string().default("main")
});

export const projectCreateSchema = z.object({
  name: z.string().min(2),
  type: z.enum(["static-website", "dynamic-website", "saas", "erp-crm", "api", "pwa"]),
  sourceMode: z.enum(["new-project", "existing-repository", "import"]),
  repository: repositorySchema.optional(),
  lifecycleStatus: z.enum(["development", "staging", "production"]).default("development"),
  environments: z.array(z.enum(["development", "staging", "production"])).min(1).default(["development"]),
  productionProtected: z.boolean().default(true),
  healthPath: z.string().default("/api/health")
}).superRefine((value, ctx) => {
  if (value.sourceMode === "existing-repository" && !value.repository) {
    ctx.addIssue({ code: "custom", message: "repository is required for existing-repository mode", path: ["repository"] });
  }
});

export const projectManifestSchema = projectCreateSchema.and(z.object({
  id: z.string().uuid(),
  createdAt: z.string().datetime(),
  updatedAt: z.string().datetime()
}));

export type ProjectCreateInput = z.input<typeof projectCreateSchema>;
export type ProjectManifest = z.infer<typeof projectManifestSchema>;