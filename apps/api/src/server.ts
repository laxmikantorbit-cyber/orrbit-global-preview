import Fastify from "fastify";
import { createLocalProvisioningPlan } from "@orrbit/ai-orchestrator";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";
import { projectCreateSchema } from "@orrbit/project-manifest";
import { MemoryProjectRegistry } from "@orrbit/project-registry";

const app = Fastify({ logger: true });
const registry = new MemoryProjectRegistry();

app.get("/api/health", async () => ({
  status: "healthy",
  service: "orrbit-ai-control-plane",
  version: "0.1.0"
}));

app.get("/api/projects", async () => ({ projects: await registry.list() }));

app.get<{ Params: { id: string } }>("/api/projects/:id", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return project;
});

app.post("/api/projects", async (request, reply) => {
  const parsed = projectCreateSchema.safeParse(request.body);
  if (!parsed.success) return reply.code(400).send({ error: "invalid_project", issues: parsed.error.issues });
  return reply.code(201).send(await registry.create(parsed.data));
});

app.post<{ Body: { prompt: string } }>("/api/project-plans", async (request, reply) => {
  const prompt = request.body?.prompt?.trim();
  if (!prompt || prompt.length < 10) return reply.code(400).send({ error: "prompt_too_short" });
  return reply.code(201).send(createLocalProvisioningPlan(prompt));
});

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => ({ risk: classifyRisk(request.body), requiresApproval: requiresApproval(request.body) })
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });