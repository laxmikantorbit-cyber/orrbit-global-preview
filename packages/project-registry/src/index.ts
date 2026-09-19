import { randomUUID } from "node:crypto";
import type { Pool } from "pg";
import { projectCreateSchema, type ProjectCreateInput, type ProjectManifest } from "@orrbit/project-manifest";

export interface ProjectRegistry {
  list(): Promise<ProjectManifest[]>;
  get(id: string): Promise<ProjectManifest | undefined>;
  create(input: ProjectCreateInput): Promise<ProjectManifest>;
}

export class MemoryProjectRegistry implements ProjectRegistry {
  private readonly projects = new Map<string, ProjectManifest>();

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
    return record;
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
}
