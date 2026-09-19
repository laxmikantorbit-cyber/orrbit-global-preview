import Fastify from "fastify";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";

const app = Fastify({ logger: true });

app.get("/api/health", async () => ({
  status: "healthy",
  service: "orrbit-ai-control-plane",
  version: "0.1.0"
}));

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => {
    const context = request.body;
    return {
      risk: classifyRisk(context),
      requiresApproval: requiresApproval(context)
    };
  }
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });