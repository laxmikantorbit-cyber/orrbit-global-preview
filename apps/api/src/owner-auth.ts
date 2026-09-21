import { createHash, randomBytes, randomUUID, scryptSync, timingSafeEqual } from "node:crypto";
import type { Pool } from "pg";

export type OwnerIdentity = {
  id: string;
  email: string;
  createdAt: string;
};

type OwnerAccount = OwnerIdentity & { passwordHash: string };
type OwnerSession = {
  id: string;
  ownerId: string;
  tokenHash: string;
  expiresAt: string;
  createdAt: string;
};

const SESSION_TTL_SECONDS = 60 * 60 * 12;
export const OWNER_SESSION_COOKIE = "orrbit_cp_session";

function normalizeEmail(value: string) {
  return value.trim().toLowerCase();
}

function hashToken(token: string) {
  return createHash("sha256").update(token).digest("hex");
}

function hashPassword(password: string) {
  const salt = randomBytes(16);
  const derived = scryptSync(password, salt, 64);
  return `scrypt$${salt.toString("hex")}$${derived.toString("hex")}`;
}
function verifyPassword(password: string, encoded: string) {
  const [kind, saltHex, hashHex] = encoded.split("$");
  if (kind !== "scrypt" || !saltHex || !hashHex) return false;
  const expected = Buffer.from(hashHex, "hex");
  const actual = scryptSync(password, Buffer.from(saltHex, "hex"), expected.length);
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}

function validateCredentials(email: string, password: string) {
  const normalized = normalizeEmail(email);
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized)) throw new Error("valid_owner_email_required");
  if (password.length < 12) throw new Error("owner_password_minimum_12_characters");
  return normalized;
}

export function parseCookie(header: string | undefined, name: string) {
  if (!header) return undefined;
  for (const part of header.split(";")) {
    const [key, ...rest] = part.trim().split("=");
    if (key === name) return decodeURIComponent(rest.join("="));
  }
  return undefined;
}

export function createOwnerSessionCookie(token: string, secure: boolean) {
  const parts = [
    `${OWNER_SESSION_COOKIE}=${encodeURIComponent(token)}`,
    "Path=/",
    `Max-Age=${SESSION_TTL_SECONDS}`,
    "HttpOnly",
    "SameSite=Strict"
  ];
  if (secure) parts.push("Secure");
  return parts.join("; ");
}

export function clearOwnerSessionCookie(secure: boolean) {
  const parts = [`${OWNER_SESSION_COOKIE}=`, "Path=/", "Max-Age=0", "HttpOnly", "SameSite=Strict"];
  if (secure) parts.push("Secure");
  return parts.join("; ");
}
export class OwnerAuthStore {
  private memoryOwner: OwnerAccount | null = null;
  private memorySessions = new Map<string, OwnerSession>();

  constructor(private readonly pool: Pool | null) {}

  async getOwner(): Promise<OwnerAccount | undefined> {
    if (!this.pool) return this.memoryOwner ?? undefined;
    const result = await this.pool.query<{
      id: string;
      email: string;
      password_hash: string;
      created_at: Date;
    }>("SELECT id, email, password_hash, created_at FROM owner_accounts ORDER BY created_at ASC LIMIT 1");
    const row = result.rows[0];
    if (!row) return undefined;
    return {
      id: row.id,
      email: row.email,
      passwordHash: row.password_hash,
      createdAt: new Date(row.created_at).toISOString()
    };
  }

  async isConfigured() {
    return Boolean(await this.getOwner());
  }

  async setup(email: string, password: string): Promise<OwnerIdentity> {
    if (await this.getOwner()) throw new Error("owner_already_configured");
    const normalized = validateCredentials(email, password);
    const account: OwnerAccount = {
      id: randomUUID(),
      email: normalized,
      passwordHash: hashPassword(password),
      createdAt: new Date().toISOString()
    };
    if (!this.pool) {
      this.memoryOwner = account;
    } else {
      await this.pool.query(
        "INSERT INTO owner_accounts (id, email, password_hash, created_at) VALUES ($1,$2,$3,$4)",
        [account.id, account.email, account.passwordHash, account.createdAt]
      );
    }
    return { id: account.id, email: account.email, createdAt: account.createdAt };
  }
  async authenticate(email: string, password: string): Promise<OwnerIdentity | undefined> {
    const account = await this.getOwner();
    if (!account || normalizeEmail(email) !== account.email || !verifyPassword(password, account.passwordHash)) return undefined;
    return { id: account.id, email: account.email, createdAt: account.createdAt };
  }

  async createSession(owner: OwnerIdentity) {
    const token = randomBytes(32).toString("base64url");
    const now = new Date();
    const session: OwnerSession = {
      id: randomUUID(),
      ownerId: owner.id,
      tokenHash: hashToken(token),
      expiresAt: new Date(now.getTime() + SESSION_TTL_SECONDS * 1000).toISOString(),
      createdAt: now.toISOString()
    };
    if (!this.pool) {
      this.memorySessions.set(session.tokenHash, session);
    } else {
      await this.pool.query(
        `INSERT INTO owner_sessions (id, owner_id, token_hash, expires_at, created_at, last_seen_at)
         VALUES ($1,$2,$3,$4,$5,$5)`,
        [session.id, session.ownerId, session.tokenHash, session.expiresAt, session.createdAt]
      );
    }
    return { token, expiresAt: session.expiresAt };
  }

  async getSessionOwner(token: string | undefined): Promise<OwnerIdentity | undefined> {
    if (!token) return undefined;
    const tokenHash = hashToken(token);
    if (!this.pool) {
      const session = this.memorySessions.get(tokenHash);
      if (!session || new Date(session.expiresAt).getTime() <= Date.now()) {
        this.memorySessions.delete(tokenHash);
        return undefined;
      }
      const owner = this.memoryOwner;
      if (!owner || owner.id !== session.ownerId) return undefined;
      return { id: owner.id, email: owner.email, createdAt: owner.createdAt };
    }
    const result = await this.pool.query<{
      owner_id: string;
      email: string;
      owner_created_at: Date;
      session_id: string;
    }>(
      `SELECT a.id AS owner_id, a.email, a.created_at AS owner_created_at, s.id AS session_id
       FROM owner_sessions s
       JOIN owner_accounts a ON a.id=s.owner_id
       WHERE s.token_hash=$1 AND s.expires_at > NOW()
       LIMIT 1`,
      [tokenHash]
    );
    const row = result.rows[0];
    if (!row) return undefined;
    await this.pool.query("UPDATE owner_sessions SET last_seen_at=NOW() WHERE id=$1", [row.session_id]);
    return { id: row.owner_id, email: row.email, createdAt: new Date(row.owner_created_at).toISOString() };
  }

  async revokeSession(token: string | undefined) {
    if (!token) return;
    const tokenHash = hashToken(token);
    if (!this.pool) {
      this.memorySessions.delete(tokenHash);
      return;
    }
    await this.pool.query("DELETE FROM owner_sessions WHERE token_hash=$1", [tokenHash]);
  }

  async revokeAllSessions(ownerId: string) {
    if (!this.pool) {
      for (const [key, session] of this.memorySessions) {
        if (session.ownerId === ownerId) this.memorySessions.delete(key);
      }
      return;
    }
    await this.pool.query("DELETE FROM owner_sessions WHERE owner_id=$1", [ownerId]);
  }
}
