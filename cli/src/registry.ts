import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { HoneError } from "./errors.js";

export type FetchLike = (url: string | URL) => Promise<Response>;

export interface RegistryFile {
  path: string;
  type: string;
}

export interface RegistryItem {
  name: string;
  type: string;
  files: RegistryFile[];
}

export interface Registry {
  items: RegistryItem[];
}

// registry の取得元。base はリポジトリのルート（URL かローカルパス）で、
// <base>/registry.json と <base>/registry/<files[].path> を読む
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
    } catch {
      throw new HoneError(`registry.json を JSON として読めませんでした: ${this.base}`);
    }
    const items = (json as { items?: unknown }).items;
    if (!Array.isArray(items)) {
      throw new HoneError(`registry.json に items がありません: ${this.base}`);
    }
    return { items: items as RegistryItem[] };
  }

  readFile(filePath: string): Promise<Buffer> {
    return this.read(`registry/${filePath}`);
  }

  private async read(relative: string): Promise<Buffer> {
    if (/^https?:\/\//i.test(this.base)) {
      const url = new URL(relative, this.base.endsWith("/") ? this.base : `${this.base}/`);
      const res = await this.fetchFn(url);
      if (!res.ok) {
        throw new HoneError(`取得に失敗しました（${res.status}）: ${url.href}`);
      }
      return Buffer.from(await res.arrayBuffer());
    }
    const baseDir = this.base.startsWith("file:")
      ? fileURLToPath(this.base)
      : path.resolve(this.cwd, this.base);
    const file = path.join(baseDir, relative);
    try {
      return await readFile(file);
    } catch {
      throw new HoneError(`ファイルを読めませんでした: ${file}`);
    }
  }
}

// 項目名の照合は大文字小文字を区別しない
export function findItem(registry: Registry, name: string): RegistryItem {
  const item = registry.items.find((i) => i.name.toLowerCase() === name.toLowerCase());
  if (!item) {
    throw new HoneError(`registry に ${name} がありません`);
  }
  return item;
}
