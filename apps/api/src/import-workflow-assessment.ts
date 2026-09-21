import type {
  ImportExecutionJob,
  ProjectImportWorkspace,
  SourceAcquisitionRecord
} from "@orrbit/project-importer";
import type { SourceBuildJob } from "./source-build-runner.js";

export type ImportWorkflowAssessment = {
  workspaceId: string;
  projectId: string | null;
  complete: boolean;
  realExecutionAuthorized: boolean;
  steps: Array<{
    key: string;
    label: string;
    ready: boolean;
    evidence: string[];
  }>;
  blockers: string[];
  protectionsVerified: boolean;
};

export function assessImportWorkflow(input: {
  workspace: ProjectImportWorkspace;
  acquisition?: SourceAcquisitionRecord | null;
  build?: SourceBuildJob | null;
  execution?: ImportExecutionJob | null;
  realExecutionAuthorized: boolean;
}): ImportWorkflowAssessment {
  const { workspace, acquisition, build, execution } = input;
  const allRoutes = workspace.routeCapture.every((item) => item.status === "captured");
  const allModules = workspace.moduleCapture.every((item) => item.status === "captured");
  const manifestReviewed = workspace.captureChecklist.some((item) =>
    item.key === "manifest-review" && item.status === "captured");
  const captureReady = workspace.deployGate.canDeploy && workspace.status === "capture_complete";
  const acquisitionReady = acquisition?.status === "acquired";
  const buildReady = build?.status === "preview_ready" && Boolean(build.preview) && build.blockers.length === 0;
  const executionReady = execution?.status === "preview_ready"
    && execution.parity.routes.missing.length === 0
    && execution.parity.modules.missing.length === 0
    && Boolean(execution.preview);

  const protectionValues = [
    acquisition?.protections.isolatedInboxOnly,
    acquisition?.protections.productionLocked,
    acquisition?.protections.dnsLocked,
    acquisition?.protections.livePaymentLocked,
    acquisition?.protections.liveDatabaseLocked,
    acquisition?.protections.customerDataLocked,
    build?.protections.hostExecutionDisabled,
    build?.protections.productionLocked,
    build?.protections.dnsLocked,
    build?.protections.livePaymentLocked,
    build?.protections.liveDatabaseLocked,
    build?.protections.customerDataLocked,
    execution?.protections.productionLocked,
    execution?.protections.dnsLocked,
    execution?.protections.livePaymentLocked,
    execution?.protections.liveDatabaseLocked,
    execution?.protections.customerDataLocked
  ];
  const protectionsVerified = protectionValues.length > 0 && protectionValues.every((value) => value === true);

  const steps = [
    {
      key: "source-reference",
      label: "Source reference confirmed",
      ready: workspace.sourceReferenceStatus === "provided",
      evidence: workspace.sourceReferenceStatus === "provided" ? [workspace.sourceRef] : []
    },
    {
      key: "capture",
      label: "Routes, modules and manifest capture complete",
      ready: allRoutes && allModules && manifestReviewed && captureReady,
      evidence: [
        `routes:${workspace.routeCapture.filter((item) => item.status === "captured").length}/${workspace.routeCapture.length}`,
        `modules:${workspace.moduleCapture.filter((item) => item.status === "captured").length}/${workspace.moduleCapture.length}`,
        `manifest_reviewed:${manifestReviewed}`
      ]
    },
    {
      key: "source-acquisition",
      label: "Source archive acquired in isolated inbox",
      ready: acquisitionReady,
      evidence: acquisitionReady && acquisition ? [acquisition.sha256, acquisition.sourceType] : []
    },
    {
      key: "isolated-build",
      label: "Actual source built in isolated sandbox",
      ready: buildReady,
      evidence: buildReady && build ? [build.framework, build.artifactDirectory ?? "", build.preview?.url ?? ""] : []
    },
    {
      key: "parity-preview",
      label: "Captured inventory parity preview verified",
      ready: executionReady,
      evidence: executionReady && execution ? [
        `routes:${execution.parity.routes.imported}/${execution.parity.routes.expected}`,
        `modules:${execution.parity.modules.imported}/${execution.parity.modules.expected}`,
        execution.preview?.reference ?? ""
      ] : []
    },
    {
      key: "protections",
      label: "Production-impact protections verified",
      ready: protectionsVerified,
      evidence: protectionsVerified ? [
        "production_locked",
        "dns_locked",
        "live_payment_locked",
        "live_database_locked",
        "customer_data_locked",
        "host_execution_disabled"
      ] : []
    }
  ];

  const blockers = steps.filter((step) => !step.ready).map((step) => step.key);
  return {
    workspaceId: workspace.id,
    projectId: workspace.projectId ?? null,
    complete: blockers.length === 0,
    realExecutionAuthorized: input.realExecutionAuthorized,
    steps,
    blockers,
    protectionsVerified
  };
}
