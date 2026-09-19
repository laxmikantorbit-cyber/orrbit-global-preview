export interface DeploymentResult {
  providerDeploymentId: string;
  url?: string;
  revision?: string;
}

export interface FrontendProvider {
  createPreview(projectId: string, ref: string): Promise<DeploymentResult>;
  deployProduction(projectId: string, ref: string): Promise<DeploymentResult>;
  rollback(projectId: string, deploymentId: string): Promise<DeploymentResult>;
}

export interface BackendProvider {
  deploy(projectId: string, imageRef: string): Promise<DeploymentResult>;
  rollback(projectId: string, revision: string): Promise<DeploymentResult>;
  health(projectId: string): Promise<boolean>;
}