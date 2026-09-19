import { randomUUID } from "node:crypto";
import type { Pool } from "pg";
import { projectCreateSchema, type ProjectCreateInput, type ProjectManifest } from "@orrbit/project-manifest";

export type ProjectEnvironmentName = ProjectManifest["environments"][number];
export type ProjectEnvironmentStatus = "unconfigured" | "planned" | "ready" | "degraded";

export interface ProjectEnvironment {
  id: string;
  projectId: string;
  environmentName: ProjectEnvironmentName;
  status: ProjectEnvironmentStatus;
  frontendProvider?: string;
  backendProvider?: string;
  databaseProvider?: string;
  region?: string;
}

export interface ProjectEnvironmentUpdate {
  status?: ProjectEnvironmentStatus;
  frontendProvider?: string | null;
  backendProvider?: string | null;
  databaseProvider?: string | null;
  region?: string | null;
}

export interface ProjectRegistry {
  list(): Promise<ProjectManifest[]>;
  get(id: string): Promise<ProjectManifest | undefined>;
  create(input: ProjectCreateInput): Promise<ProjectManifest>;
  listEnvironments(projectId: string): Promise<ProjectEnvironment[]>;
  updateEnvironment(projectId: string, environment: ProjectEnvironmentName, input: ProjectEnvironmentUpdate): Promise<ProjectEnvironment | undefined>;
}

export class MemoryProjectRegistry implements ProjectRegistry {
  private readonly projects = new Map<string, ProjectManifest>();
  private readonly environments = new Map<string, ProjectEnvironment[]>();

  async list(): Promise<ProjectManifest[]> {
    return [...this.projects.values()];
  }

  async get(id: string): Promise<ProjectManifest | undefined> {
    return this.projects.get(id);
  }

  async create(input: ProjectCreateInput): Promise<ProjectManifest> {
    const validated = projectCreateSchema.parse(input);
    const now = new Date().toISOString();
    const record = { ...validated, id: randomUUID(), createdAt: now, updatedAt: now } as ProjectManifest;
    this.projects.set(record.id, record);
    this.environments.set(record.id, validated.environments.map((environmentName) => ({
      id: randomUUID(),
      projectId: record.id,
      environmentName,
      status: "unconfigured"
    })));
    return record;
  }

  async listEnvironments(projectId: string): Promise<ProjectEnvironment[]> {
    return this.environments.get(projectId) ?? [];
  }

  async updateEnvironment(projectId: string, environment: ProjectEnvironmentName, input: ProjectEnvironmentUpdate): Promise<ProjectEnvironment | undefined> {
    const records = this.environments.get(projectId) ?? [];
    const index = records.findIndex((record) => record.environmentName === environment);
    if (index < 0) return undefined;
    const updated = {
      ...records[index],
      ...input,
      frontendProvider: input.frontendProvider === null ? undefined : input.frontendProvider ?? records[index].frontendProvider,
      backendProvider: input.backendProvider === null ? undefined : input.backendProvider ?? records[index].backendProvider,
      databaseProvider: input.databaseProvider === null ? undefined : input.databaseProvider ?? records[index].databaseProvider,
      region: input.region === null ? undefined : input.region ?? records[index].region
    };
    records[index] = updated;
    this.environments.set(projectId, records);
    return updated;
  }
}

type ProjectRow = {
  id: string; name: string; project_type: ProjectManifest["type"]; source_mode: ProjectManifest["sourceMode"];
  lifecycle_status: ProjectManifest["lifecycleStatus"]; repository_full_name: string | null;
  default_branch: string; production_protected: boolean; health_path: string;
  created_at: Date | string; updated_at: Date | string; environments: string[];
};

function rowToManifest(row: ProjectRow): ProjectManifest {
  return {
    id: row.id,
    name: row.name,
    type: row.project_type,
    sourceMode: row.source_mode,
    lifecycleStatus: row.lifecycle_status,
    environments: row.environments as ProjectManifest["environments"],
    productionProtected: row.production_protected,
    healthPath: row.health_path,
    repository: row.repository_full_name
      ? { fullName: row.repository_full_name, defaultBranch: row.default_branch }
      : undefined,
    createdAt: new Date(row.created_at).toISOString(),
    updatedAt: new Date(row.updated_at).toISOString()
  };
}

const selectProjects = `
  SELECT p.*,
    COALESCE(array_agg(pe.environment_name ORDER BY pe.environment_name)
      FILTER (WHERE pe.environment_name IS NOT NULL), ARRAY[]::text[]) AS environments
  FROM projects p
  LEFT JOIN project_environments pe ON pe.project_id = p.id
`;

type EnvironmentRow = {
  id: string;
  project_id: string;
  environment_name: ProjectEnvironmentName;
  status: ProjectEnvironmentStatus;
  frontend_provider: string | null;
  backend_provider: string | null;
  database_provider: string | null;
  region: string | null;
};

function rowToEnvironment(row: EnvironmentRow): ProjectEnvironment {
  return {
    id: row.id,
    projectId: row.project_id,
    environmentName: row.environment_name,
    status: row.status,
    frontendProvider: row.frontend_provider ?? undefined,
    backendProvider: row.backend_provider ?? undefined,
    databaseProvider: row.database_provider ?? undefined,
    region: row.region ?? undefined
  };
}

export class PostgresProjectRegistry implements ProjectRegistry {
  private readonly pool: Pool;

  constructor(pool: Pool) {
    this.pool = pool;
  }

  async list(): Promise<ProjectManifest[]> {
    const result = await this.pool.query<ProjectRow>(
      `${selectProjects} GROUP BY p.id ORDER BY p.created_at DESC`
    );
    return result.rows.map(rowToManifest);
  }

  async get(id: string): Promise<ProjectManifest | undefined> {
    const result = await this.pool.query<ProjectRow>(
      `${selectProjects} WHERE p.id = $1 GROUP BY p.id`, [id]
    );
    return result.rows[0] ? rowToManifest(result.rows[0]) : undefined;
  }

  async create(input: ProjectCreateInput): Promise<ProjectManifest> {
    const validated = projectCreateSchema.parse(input);
    const client = await this.pool.connect();
    const id = randomUUID();
    const now = new Date().toISOString();
    try {
      await client.query("BEGIN");
      await client.query(
        `INSERT INTO projects
          (id, name, project_type, source_mode, lifecycle_status, repository_full_name,
           default_branch, production_protected, health_path, created_at, updated_at)
         VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$10)`,
        [id, validated.name, validated.type, validated.sourceMode, validated.lifecycleStatus,
          validated.repository?.fullName ?? null, validated.repository?.defaultBranch ?? "main",
          validated.productionProtected, validated.healthPath, now]
      );
      for (const environment of validated.environments) {
        await client.query(
          `INSERT INTO project_environments (id, project_id, environment_name)
           VALUES ($1,$2,$3)`,
          [randomUUID(), id, environment]
        );
      }
      await client.query("COMMIT");
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally {
      client.release();
    }
    return { ...validated, id, createdAt: now, updatedAt: now } as ProjectManifest;
  }

  async listEnvironments(projectId: string): Promise<ProjectEnvironment[]> {
    const result = await this.pool.query<EnvironmentRow>(
      `SELECT id, project_id, environment_name, status, frontend_provider,
              backend_provider, database_provider, region
       FROM project_environments
       WHERE project_id = $1
       ORDER BY environment_name`,
      [projectId]
    );
    return result.rows.map(rowToEnvironment);
  }

  async updateEnvironment(projectId: string, environment: ProjectEnvironmentName, input: ProjectEnvironmentUpdate): Promise<ProjectEnvironment | undefined> {
    const result = await this.pool.query<EnvironmentRow>(
      `UPDATE project_environments
       SET status = COALESCE($3, status),
           frontend_provider = CASE WHEN $4::boolean THEN $5 ELSE frontend_provider END,
           backend_provider = CASE WHEN $6::boolean THEN $7 ELSE backend_provider END,
           database_provider = CASE WHEN $8::boolean THEN $9 ELSE database_provider END,
           region = CASE WHEN $10::boolean THEN $11 ELSE region END
       WHERE project_id = $1 AND environment_name = $2
       RETURNING id, project_id, environment_name, status, frontend_provider,
                 backend_provider, database_provider, region`,
      [
        projectId, environment, input.status ?? null,
        Object.prototype.hasOwnProperty.call(input, "frontendProvider"), input.frontendProvider ?? null,
        Object.prototype.hasOwnProperty.call(input, "backendProvider"), input.backendProvider ?? null,
        Object.prototype.hasOwnProperty.call(input, "databaseProvider"), input.databaseProvider ?? null,
        Object.prototype.hasOwnProperty.call(input, "region"), input.region ?? null
      ]
    );
    return result.rows[0] ? rowToEnvironment(result.rows[0]) : undefined;
  }
}
