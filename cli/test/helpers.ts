import { mkdir, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

// リポジトリのルート（registry.json と registry/ がある階層）。ローカル registry として使う
export const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

export async function makeUnityProject(tmp: string): Promise<string> {
  const root = path.join(tmp, "project");
  await mkdir(path.join(root, "Assets"), { recursive: true });
  await mkdir(path.join(root, "ProjectSettings"), { recursive: true });
  return root;
}

export async function listFiles(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { recursive: true, withFileTypes: true });
  return entries
    .filter((e) => e.isFile())
    .map((e) => path.relative(dir, path.join(e.parentPath, e.name)).split(path.sep).join("/"))
    .sort();
}
