import { randomUUID } from "node:crypto";
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