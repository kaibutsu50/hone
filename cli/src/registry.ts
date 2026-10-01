import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { HoneError, reason } from "./errors.js";

export type FetchLike = (url: string | URL) => Promise<Response>;

export interface RegistryFile {
  path: string;
  type: string;
}

export interface RegistryItem {
  name: string;
  type: string;
  description?: string;
  files: RegistryFile[];
  registryDependencies?: string[];
  dependencies?: string[];
}

export interface Registry {
  items: RegistryItem[];
}

// registry の取得元。base はリポジトリのルート（URL かローカルパス）で、
// <base>/registry.json と <base>/registry/<files[].path> を読む。
// ローカルは絶対パスか file: URL（file: は絶対のみ）。相対パスは cwd 基準で解決する
export class RegistrySource {
  constructor(
    private readonly base: string,
    private readonly cwd: string,
    private readonly fetchFn: FetchLike,
  ) {}

  async loadRegistry(): Promise<Registry> {
    const bytes = await this.read("registry.json");
    let json: unknown;
    try {
      json = JSON.parse(bytes.toString("utf8"));
    } catch (e) {
      throw new HoneError(`registry.json を JSON として読めませんでした（${reason(e)}）: ${this.base}`, {
        cause: e,
      });
    }
    const items = (json as { items?: unknown } | null)?.items;
    if (!Array.isArray(items) || !items.every(isItem)) {
      throw new HoneError(
        `registry.json の形式が不正です（items[].name と items[].type と items[].files[].path は文字列、registryDependencies と dependencies は文字列の配列）: ${this.base}`,
      );
    }
    return { items };
  }

  readFile(filePath: string): Promise<Buffer> {
    return this.read(`registry/${filePath}`);
  }

  private async read(relative: string): Promise<Buffer> {
    if (/^https?:\/\//i.test(this.base)) {
      return this.fetchBytes(relative);
    }
    let baseDir: string;
    try {
      baseDir = this.base.startsWith("file:")
        ? fileURLToPath(this.base)
        : path.resolve(this.cwd, this.base);
    } catch (e) {
      throw new HoneError(`file: URL は絶対パスで指定してください: ${this.base}`, { cause: e });
    }
    const file = path.join(baseDir, relative);
    try {
      return await readFile(file);
    } catch (e) {
      throw new HoneError(`ファイルを読めませんでした（${reason(e)}）: ${file}`, { cause: e });
    }
  }

  private async fetchBytes(relative: string): Promise<Buffer> {
    const url = new URL(relative, this.base.endsWith("/") ? this.base : `${this.base}/`);
    let status: number | undefined;
    let body: Buffer | undefined;
    try {
      const res = await this.fetchFn(url);
      status = res.status;
      if (res.ok) body = Buffer.from(await res.arrayBuffer());
    } catch (e) {
      throw new HoneError(`取得に失敗しました（${reason(e)}）: ${url.href}`, { cause: e });
    }
    if (!body) {
      throw new HoneError(
        `取得に失敗しました（${status}）: ${url.href}。--registry か hone.json の registry を確認してください`,
      );
    }
    return body;
  }
}

function isItem(value: unknown): value is RegistryItem {
  const item = value as Partial<RegistryItem> | null;
  return (
    typeof item === "object" &&
    item !== null &&
    typeof item.name === "string" &&
    typeof item.type === "string" &&
    Array.isArray(item.files) &&
    item.files.every((f) => typeof (f as Partial<RegistryFile> | null)?.path === "string") &&
    isOptionalStringArray(item.registryDependencies) &&
    isOptionalStringArray(item.dependencies)
  );
}

function isOptionalStringArray(value: unknown): boolean {
  return value === undefined || (Array.isArray(value) && value.every((v) => typeof v === "string"));
}

// registry の name は PascalCase だが、CLI の引数は button でも Button でも引けるようにする
export function findItem(registry: Registry, name: string): RegistryItem {
  const item = registry.items.find((i) => i.name.toLowerCase() === name.toLowerCase());
  if (!item) {
    throw new HoneError(`registry に ${name} がありません`);
  }
  return item;
}
