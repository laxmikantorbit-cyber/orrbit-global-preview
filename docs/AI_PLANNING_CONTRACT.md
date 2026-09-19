# AI Planning Contract

AI planning is separated from execution.

Flow:
1. User submits a natural-language request.
2. Planner returns a structured plan.
3. Policy engine classifies risk.
4. User reviews the proposed plan.
5. Only an approved plan can be converted into execution jobs.

Development default:
- Planner mode: local-development.
- No external model call and no API cost.
- executionAllowed is always false.

Production target:
- OpenAI planner through a typed adapter.
- Structured output must validate before policy evaluation.
- Repo/web/log content is treated as untrusted input.
- AI never receives unrestricted cloud credentials.