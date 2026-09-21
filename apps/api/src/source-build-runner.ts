import { spawn } from "node:child_process";
import { access, cp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { dirname, extname, isAbsolute, join, relative, resolve } from "node:path";
import { randomUUID } from "node:crypto";
import type { SourceAcquisitionRecord } from "@orrbit/project-importer";

export type SourceBuildFramework = "vite" | "next" | "react-scripts" | "astro" | "static" | "unknown";
export type SourceBuildPackageManager = "npm" | "pnpm" | "yarn";
export type SourceBuildStageStatus = "pending" | "succeeded" | "failed" | "blocked";
export type SourceBuildStatus = "queued" | "detecting" | "installing" | "building" | "verifying" | "preview_ready" | "failed" | "blocked" | "reset";

export type SourceBuildJob = {
  id: string;
  acquisitionId: string;
  workspaceId: string;
  projectId: string;
  status: SourceBuildStatus;
  framework: SourceBuildFramework;
  packageManager: SourceBuildPackageManager;
  projectSubdir: string;
  installCommand: string;
  buildCommand: string;
  stages: Array<{ name: "detect" | "install" | "build" | "verify" | "preview"; status: SourceBuildStageStatus; detail?: string }>;
  artifactDirectory: string | null;
  preview: null | {
    containerName: string;
    networkName: string;
    url: string;
    hostPort: number;
    kind: "static";
  };
  logs: string[];
  blockers: string[];
  protections: {
    dockerSandboxRequired: true;
    hostExecutionDisabled: true;
    buildNetworkDisabled: true;
    productionLocked: true;
    dnsLocked: true;
    livePaymentLocked: true;
    liveDatabaseLocked: true;
    customerDataLocked: true;
  };
  createdAt: string;
  updatedAt: string;
};

type CommandResult = { code: number; stdout: string; stderr: string; timedOut: boolean };

function redactBuildLog(value: string) {
  return value
    .replace(/(authorization:\s*bearer\s+)[^\s]+/gi, "$1[REDACTED]")
    .replace(/((?:api[_-]?key|token|secret|password)\s*[:=]\s*)[^\s'"]+/gi, "$1[REDACTED]")
    .slice(-12000);
}

function runCommand(command: string, args: string[], timeoutMs = 120000): Promise<CommandResult> {
  return new Promise((resolvePromise) => {
    const child = spawn(command, args, { windowsHide: true, shell: false });
    let stdout = "";
    let stderr = "";
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      child.kill();
      settled = true;
      resolvePromise({ code: -1, stdout: redactBuildLog(stdout), stderr: redactBuildLog(stderr), timedOut: true });
    }, timeoutMs);
    child.stdout.on("data", (data) => { stdout += String(data); });
    child.stderr.on("data", (data) => { stderr += String(data); });
    child.on("error", (error) => {
      if (settled) return;
      clearTimeout(timer);
      settled = true;
      resolvePromise({ code: -1, stdout: redactBuildLog(stdout), stderr: redactBuildLog(String(error)), timedOut: false });
    });
    child.on("close", (code) => {
      if (settled) return;
      clearTimeout(timer);
      settled = true;
      resolvePromise({ code: code ?? -1, stdout: redactBuildLog(stdout), stderr: redactBuildLog(stderr), timedOut: false });
    });
  });
}

async function pathExists(path: string) {
  try { await access(path); return true; } catch { return false; }
}

function safeRelativePath(base: string, child: string) {
  const rel = relative(base, child);
  return !rel.startsWith("..") && !isAbsolute(rel);
}

function acquisitionFolder(importInboxRoot: string, workspaceId: string, acquisitionId: string) {
  return resolve(importInboxRoot, workspaceId, acquisitionId);
}

async function stripSensitiveBuildFiles(root: string) {
  const removed: string[] = [];
  async function walk(current: string) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const full = join(current, entry.name);
      if (entry.isDirectory()) {
        if (entry.name === "node_modules" || entry.name === ".git") continue;
        await walk(full);
        continue;
      }
      const lower = entry.name.toLowerCase();
      const sensitive = lower === ".env" || lower.startsWith(".env.") ||
        lower === "id_rsa" || lower === "id_ed25519" ||
        lower === "credentials.json" || lower === "service-account.json" ||
        [".pem", ".key", ".p12", ".pfx"].includes(extname(lower));
      if (sensitive) {
        await rm(full, { force: true });
        removed.push(relative(root, full).replaceAll("\\", "/"));
      }
    }
  }
  await walk(root);
  return removed;
}

async function detectSourceBuild(record: SourceAcquisitionRecord, sourceRoot: string) {
  const packagePath = record.inventory.packageJsonPath?.replaceAll("\\", "/");
  const projectSubdir = packagePath ? dirname(packagePath) : ".";
  const projectRoot = resolve(sourceRoot, projectSubdir);
  if (!safeRelativePath(sourceRoot, projectRoot) && projectRoot !== sourceRoot) throw new Error("project_root_escape_blocked");
  const packageFile = join(projectRoot, "package.json");
  const hasPackage = await pathExists(packageFile);
  const hasIndex = await pathExists(join(projectRoot, "index.html"));
  let packageJson: any = {};
  if (hasPackage) packageJson = JSON.parse((await readFile(packageFile, "utf8")).replace(/^\uFEFF/, ""));
  const deps = { ...(packageJson.dependencies ?? {}), ...(packageJson.devDependencies ?? {}) };
  const buildScript = String(packageJson.scripts?.build ?? "");
  let framework: SourceBuildFramework = "unknown";
  if ("vite" in deps || /\bvite\b/i.test(buildScript)) framework = "vite";
  else if ("next" in deps || /\bnext\s+build\b/i.test(buildScript)) framework = "next";
  else if ("react-scripts" in deps || /react-scripts\s+build/i.test(buildScript)) framework = "react-scripts";
  else if ("astro" in deps || /\bastro\s+build\b/i.test(buildScript)) framework = "astro";
  else if (!hasPackage && hasIndex) framework = "static";
  else if (hasIndex && !buildScript) framework = "static";

  const managerField = String(packageJson.packageManager ?? "");
  let packageManager: SourceBuildPackageManager = "npm";
  if (managerField.startsWith("pnpm@") || await pathExists(join(projectRoot, "pnpm-lock.yaml"))) packageManager = "pnpm";
  else if (managerField.startsWith("yarn@") || await pathExists(join(projectRoot, "yarn.lock"))) packageManager = "yarn";

  const hasNpmLock = await pathExists(join(projectRoot, "package-lock.json"));
  const installCommand = framework === "static" ? "none" :
    packageManager === "npm"
      ? (hasNpmLock ? "npm ci --ignore-scripts --no-audit --no-fund" : "npm install --ignore-scripts --no-audit --no-fund")
      : packageManager === "pnpm"
        ? "corepack pnpm install --ignore-scripts --frozen-lockfile"
        : "corepack yarn install --ignore-scripts --frozen-lockfile";
  const buildCommand = framework === "static" ? "none" :
    packageManager === "npm" ? "npm run build" :
      packageManager === "pnpm" ? "corepack pnpm run build" : "corepack yarn run build";

  const blockers: string[] = [];
  if (!hasPackage && !hasIndex) blockers.push("package_or_index_entry_required");
  if (framework !== "static" && !buildScript) blockers.push("build_script_missing");
  return { framework, packageManager, projectSubdir, installCommand, buildCommand, blockers };
}

export function createSourceBuildJob(record: SourceAcquisitionRecord): SourceBuildJob {
  const now = new Date().toISOString();
  return {
    id: randomUUID(),
    acquisitionId: record.id,
    workspaceId: record.workspaceId,
    projectId: record.projectId,
    status: "queued",
    framework: "unknown",
    packageManager: "npm",
    projectSubdir: ".",
    installCommand: "",
    buildCommand: "",
    stages: [
      { name: "detect", status: "pending" },
      { name: "install", status: "pending" },
      { name: "build", status: "pending" },
      { name: "verify", status: "pending" },
      { name: "preview", status: "pending" }
    ],
    artifactDirectory: null,
    preview: null,
    logs: [],
    blockers: [],
    protections: {
      dockerSandboxRequired: true,
      hostExecutionDisabled: true,
      buildNetworkDisabled: true,
      productionLocked: true,
      dnsLocked: true,
      livePaymentLocked: true,
      liveDatabaseLocked: true,
      customerDataLocked: true
    },
    createdAt: now,
    updatedAt: now
  };
}

function updateBuildStage(job: SourceBuildJob, name: SourceBuildJob["stages"][number]["name"], status: SourceBuildStageStatus, detail?: string) {
  job.stages = job.stages.map((stage) => stage.name === name ? { ...stage, status, detail } : stage);
  job.updatedAt = new Date().toISOString();
}

export async function isDockerSandboxReady() {
  const result = await runCommand("docker", ["info", "--format", "{{.ServerVersion}}"], 10000);
  return result.code === 0 && Boolean(result.stdout.trim());
}

async function stopDockerContainer(name: string | undefined | null) {
  if (!name) return;
  await runCommand("docker", ["rm", "-f", name], 20000);
}

async function stopDockerNetwork(name: string | undefined | null) {
  if (!name) return;
  await runCommand("docker", ["network", "rm", name], 20000);
}

async function findStaticArtifact(projectRoot: string, framework: SourceBuildFramework) {
  const candidates = framework === "static"
    ? [projectRoot]
    : framework === "react-scripts"
      ? [join(projectRoot, "build"), join(projectRoot, "dist")]
      : framework === "next"
        ? [join(projectRoot, "out")]
        : [join(projectRoot, "dist"), join(projectRoot, "build"), join(projectRoot, "out")];
  for (const candidate of candidates) {
    if (await pathExists(join(candidate, "index.html"))) return candidate;
  }
  return null;
}

async function launchStaticPreview(job: SourceBuildJob, artifactRoot: string, jobRoot: string) {
  const containerName = "orrbit-preview-" + job.id.slice(0, 12);
  const networkName = "orrbit-net-" + job.id.slice(0, 12);
  const configPath = join(jobRoot, "nginx.conf");
  const config = [
    "server {",
    "  listen 8080;",
    "  server_name _;",
    "  root /usr/share/nginx/html;",
    "  index index.html;",
    "  add_header Content-Security-Policy \"default-src 'self' data: blob: 'unsafe-inline' 'unsafe-eval'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'self'\" always;",
    "  add_header X-Content-Type-Options \"nosniff\" always;",
    "  add_header Referrer-Policy \"no-referrer\" always;",
    "  location / { try_files $uri $uri/ /index.html; }",
    "}"
  ].join("\n");
  await writeFile(configPath, config, "utf8");
  const network = await runCommand("docker", ["network", "create", "--internal", networkName], 30000);
  if (network.code !== 0) throw new Error("preview_network_create_failed:" + network.stderr);
  const args = [
    "run", "-d", "--name", containerName, "--network", networkName,
    "--cpus", "0.50", "--memory", "256m", "--pids-limit", "128",
    "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
    "-p", "127.0.0.1::8080",
    "-v", `${artifactRoot}:/usr/share/nginx/html:ro`,
    "-v", `${configPath}:/etc/nginx/conf.d/default.conf:ro`,
    "nginxinc/nginx-unprivileged:1.27-alpine"
  ];
  const started = await runCommand("docker", args, 120000);
  if (started.code !== 0) {
    await stopDockerNetwork(networkName);
    throw new Error("preview_container_start_failed:" + started.stderr);
  }
  const running = await runCommand("docker", ["inspect", "-f", "{{.State.Running}}", containerName], 10000);
  if (running.code !== 0 || running.stdout.trim() !== "true") {
    await stopDockerContainer(containerName);
    await stopDockerNetwork(networkName);
    throw new Error("preview_container_not_running");
  }
  const portResult = await runCommand("docker", ["port", containerName, "8080/tcp"], 10000);
  const match = portResult.stdout.match(/127\.0\.0\.1:(\d+)/);
  if (!match) {
    await stopDockerContainer(containerName);
    await stopDockerNetwork(networkName);
    throw new Error("preview_port_not_found");
  }
  const hostPort = Number(match[1]);
  return { containerName, networkName, url: `http://127.0.0.1:${hostPort}`, hostPort, kind: "static" as const };
}

export async function runSourceBuild(job: SourceBuildJob, record: SourceAcquisitionRecord, importInboxRoot: string): Promise<SourceBuildJob> {
  const acquisitionRoot = acquisitionFolder(importInboxRoot, record.workspaceId, record.id);
  const sourceRoot = resolve(acquisitionRoot, "source");
  if (record.status !== "acquired") {
    job.status = "blocked";
    job.blockers = ["source_package_not_acquired"];
    updateBuildStage(job, "detect", "blocked", "Source package must be acquired first.");
    return job;
  }
  if (!await pathExists(sourceRoot)) {
    job.status = "blocked";
    job.blockers = ["source_snapshot_missing"];
    updateBuildStage(job, "detect", "blocked", "Isolated source snapshot is missing.");
    return job;
  }

  job.status = "detecting";
  const detected = await detectSourceBuild(record, sourceRoot);
  job.framework = detected.framework;
  job.packageManager = detected.packageManager;
  job.projectSubdir = detected.projectSubdir;
  job.installCommand = detected.installCommand;
  job.buildCommand = detected.buildCommand;
  job.blockers = detected.blockers;
  updateBuildStage(job, "detect", detected.blockers.length ? "blocked" : "succeeded",
    `${detected.framework} · ${detected.packageManager} · ${detected.projectSubdir}`);
  if (detected.blockers.length) {
    job.status = "blocked";
    return job;
  }

  if (!await isDockerSandboxReady()) {
    job.status = "blocked";
    job.blockers.push("sandbox_unavailable");
    updateBuildStage(job, "install", "blocked", "Docker engine unavailable. Host execution is disabled.");
    job.logs.push("sandbox_unavailable: Docker engine is required; no host fallback was attempted.");
    return job;
  }

  const jobRoot = resolve(acquisitionRoot, "builds", job.id);
  const workspaceRoot = resolve(jobRoot, "workspace");
  await rm(jobRoot, { recursive: true, force: true });
  await mkdir(jobRoot, { recursive: true });
  await cp(sourceRoot, workspaceRoot, { recursive: true, force: true });
  const removed = await stripSensitiveBuildFiles(workspaceRoot);
  if (removed.length) job.logs.push(`sensitive_files_removed:${removed.join(",")}`);

  const projectRoot = resolve(workspaceRoot, job.projectSubdir);
  if (!safeRelativePath(workspaceRoot, projectRoot) && projectRoot !== workspaceRoot) throw new Error("build_project_root_escape_blocked");

  if (job.framework !== "static") {
    const containerName = "orrbit-build-" + job.id.slice(0, 12);
    const workDir = job.projectSubdir === "." ? "/workspace" : `/workspace/${job.projectSubdir.replaceAll("\\", "/")}`;
    const startArgs = [
      "run", "-d", "--name", containerName, "--network", "bridge",
      "--cpus", "1.00", "--memory", "1024m", "--pids-limit", "256",
      "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
      "-e", "CI=1", "-e", "COREPACK_ENABLE_DOWNLOAD_PROMPT=0",
      "-v", `${workspaceRoot}:/workspace`, "-w", workDir,
      "node:22-alpine", "sh", "-lc", "while :; do sleep 3600; done"
    ];
    const started = await runCommand("docker", startArgs, 120000);
    if (started.code !== 0) {
      job.status = "failed";
      updateBuildStage(job, "install", "failed", "Sandbox container could not start.");
      job.logs.push("sandbox_start_failed:" + started.stderr);
      return job;
    }
    try {
      job.status = "installing";
      const install = await runCommand("docker", ["exec", "-e", "CI=1", "-e", "COREPACK_ENABLE_DOWNLOAD_PROMPT=0", containerName, "sh", "-lc", job.installCommand], 300000);
      job.logs.push(`install_exit=${install.code}\n${install.stdout}\n${install.stderr}`);
      if (install.code !== 0) {
        job.status = "failed";
        updateBuildStage(job, "install", "failed", install.timedOut ? "Dependency install timed out." : "Dependency install failed.");
        return job;
      }
      updateBuildStage(job, "install", "succeeded", "Dependencies installed with lifecycle scripts disabled.");

      const disconnected = await runCommand("docker", ["network", "disconnect", "bridge", containerName], 30000);
      if (disconnected.code !== 0) {
        job.status = "failed";
        updateBuildStage(job, "build", "failed", "Could not disable build network.");
        job.logs.push("network_disconnect_failed:" + disconnected.stderr);
        return job;
      }

      job.status = "building";
      const build = await runCommand("docker", ["exec", "-e", "CI=1", containerName, "sh", "-lc", job.buildCommand], 300000);
      job.logs.push(`build_exit=${build.code}\n${build.stdout}\n${build.stderr}`);
      if (build.code !== 0) {
        job.status = "failed";
        updateBuildStage(job, "build", "failed", build.timedOut ? "Build timed out." : "Build failed inside network-disabled sandbox.");
        return job;
      }
      updateBuildStage(job, "build", "succeeded", "Build completed with container network disconnected.");
    } finally {
      await stopDockerContainer(containerName);
    }
  } else {
    updateBuildStage(job, "install", "succeeded", "Static source: dependency install not required.");
    updateBuildStage(job, "build", "succeeded", "Static source: build command not required.");
  }

  job.status = "verifying";
  const artifactRoot = await findStaticArtifact(projectRoot, job.framework);
  if (!artifactRoot) {
    job.status = "blocked";
    job.blockers.push(job.framework === "next" ? "next_runtime_preview_not_enabled" : "static_artifact_not_found");
    updateBuildStage(job, "verify", "blocked", "Build completed, but no safe static index.html artifact was found.");
    updateBuildStage(job, "preview", "blocked", "Only isolated static-output preview is enabled in this phase.");
    return job;
  }
  job.artifactDirectory = relative(workspaceRoot, artifactRoot).replaceAll("\\", "/") || ".";
  updateBuildStage(job, "verify", "succeeded", `Static artifact verified at ${job.artifactDirectory}.`);

  try {
    const preview = await launchStaticPreview(job, artifactRoot, jobRoot);
    job.preview = preview;
    job.status = "preview_ready";
    updateBuildStage(job, "preview", "succeeded", preview.url);
  } catch (error) {
    job.status = "failed";
    updateBuildStage(job, "preview", "failed", error instanceof Error ? error.message : "preview_start_failed");
    job.logs.push(error instanceof Error ? error.message : "preview_start_failed");
  }
  job.updatedAt = new Date().toISOString();
  return job;
}

export async function resetSourceBuildJob(job: SourceBuildJob, importInboxRoot: string): Promise<SourceBuildJob> {
  if (job.preview) {
    await stopDockerContainer(job.preview.containerName);
    await stopDockerNetwork(job.preview.networkName);
  }
  const root = resolve(acquisitionFolder(importInboxRoot, job.workspaceId, job.acquisitionId), "builds", job.id);
  await rm(root, { recursive: true, force: true });
  return {
    ...job,
    status: "reset",
    preview: null,
    artifactDirectory: null,
    blockers: [],
    stages: job.stages.map((stage) => ({ ...stage, status: "pending", detail: undefined })),
    logs: [...job.logs, "build_reset"],
    updatedAt: new Date().toISOString()
  };
}
