import { z } from "zod";

export const projectTypeSchema = z.enum(["static-website", "dynamic-website", "saas", "erp-crm", "api", "pwa"]);
export const environmentNameSchema = z.enum(["development", "staging", "production"]);
export const sourceModeSchema = z.enum(["new-project", "existing-repository", "import"]);

const repositorySchema = z.object({
  fullName: z.string().min(3),
  defaultBranch: z.string().default("main")
});

export const projectCreateSchema = z.object({
  name: z.string().min(2),
  type: projectTypeSchema,
  sourceMode: sourceModeSchema,
  repository: repositorySchema.optional(),
  lifecycleStatus: environmentNameSchema.default("development"),
  environments: z.array(environmentNameSchema).min(1).default(["development"]),
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

export const environmentManifestSchema = z.object({
  id: z.string().uuid(),
  projectId: z.string().uuid(),
  environmentName: environmentNameSchema,
  status: z.enum(["unconfigured", "planned", "ready", "degraded"]),
  frontendProvider: z.string().optional(),
  backendProvider: z.string().optional(),
  databaseProvider: z.string().optional(),
  storageProvider: z.string().optional(),
  region: z.string().optional()
});

export const projectPolicySchema = z.object({
  productionProtected: z.boolean(),
  nonDevelopmentConfigLocked: z.boolean(),
  realCloudProvisioningEnabled: z.boolean(),
  directProductionAiExecutionAllowed: z.literal(false)
});

export const runtimeProjectManifestSchema = z.object({
  manifestVersion: z.literal("1.0"),
  generatedAt: z.string().datetime(),
  project: projectManifestSchema,
  environments: z.array(environmentManifestSchema),
  providers: z.object({
    sourceControl: z.literal("github"),
    frontend: z.array(z.string()),
    backend: z.array(z.string()),
    database: z.array(z.string()),
    storage: z.array(z.string())
  }),
  policies: projectPolicySchema,
  businessRules: z.array(z.string()),
  designSystem: z.object({ source: z.string(), locked: z.boolean() }),
  health: z.object({ path: z.string(), requiredBeforeRelease: z.boolean() }),
  budget: z.object({ aiMonthlyLimitInr: z.number().nullable(), cloudMonthlyLimitInr: z.number().nullable() })
});

export type ProjectCreateInput = z.input<typeof projectCreateSchema>;
export type ProjectManifest = z.infer<typeof projectManifestSchema>;
export type EnvironmentManifest = z.infer<typeof environmentManifestSchema>;
export type RuntimeProjectManifest = z.infer<typeof runtimeProjectManifestSchema>;

export function buildRuntimeProjectManifest(args: {
  project: ProjectManifest;
  environments: EnvironmentManifest[];
  businessRules?: string[];
}): RuntimeProjectManifest {
  const frontend = [...new Set(args.environments.map((e) => e.frontendProvider).filter(Boolean))] as string[];
  const backend = [...new Set(args.environments.map((e) => e.backendProvider).filter(Boolean))] as string[];
  const database = [...new Set(args.environments.map((e) => e.databaseProvider).filter(Boolean))] as string[];
  return runtimeProjectManifestSchema.parse({
    manifestVersion: "1.0",
    generatedAt: new Date().toISOString(),
    project: args.project,
    environments: args.environments,
    providers: {
      sourceControl: "github",
      frontend,
      backend,
      database,
      storage: []
    },
    policies: {
      productionProtected: args.project.productionProtected,
      nonDevelopmentConfigLocked: true,
      realCloudProvisioningEnabled: false,
      directProductionAiExecutionAllowed: false
    },
    businessRules: args.businessRules ?? [],
    designSystem: { source: "project-design-system", locked: true },
    health: { path: args.project.healthPath, requiredBeforeRelease: true },
    budget: { aiMonthlyLimitInr: null, cloudMonthlyLimitInr: null }
  });
}
