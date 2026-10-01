import path from "node:path";
import { HoneError } from "./errors.js";
import {
  assertSafeRelativePath,
  createFile,
  isDirectory,
  readHoneJson,
  resolveOutput,
  toJson,
  type HoneConfig,
} from "./project.js";
import { findItem, RegistrySource, type FetchLike } from "./registry.js";

export const DEFAULT_REGISTRY = "https://raw.githubusercontent.com/kaibutsu50/hone/main/";
const DEFAULT_OUTPUT = "Assets/Hone";

export interface InitOptions {
  cwd: string;
  registry?: string;
  fetch?: FetchLike;
  log?: (message: string) => void;
}

export async function runInit(options: InitOptions): Promise<void> {
  const { cwd } = options;
  const log = options.log ?? console.log;

  const isUnityRoot =
    (await isDirectory(path.join(cwd, "Assets"))) &&
    (await isDirectory(path.join(cwd, "ProjectSettings")));
  if (!isUnityRoot) {
    throw new HoneError(
      "Unity プロジェクトのルートで実行してください（Assets/ と ProjectSettings/ がある階層）",
    );
  }

  const honeJsonPath = path.join(cwd, "hone.json");
  const existing = await readHoneJson(honeJsonPath);
  if (existing && options.registry !== undefined && options.registry !== existing.registry) {
    log(
      `hone.json があるため --registry は使わず、hone.json の registry を使います: ${existing.registry}`,
    );
  }
  const config: HoneConfig = existing ?? {
    registry: options.registry ?? DEFAULT_REGISTRY,
    output: DEFAULT_OUTPUT,
  };

  const { outputDir, output } = resolveOutput(cwd, config);

  // 書き込みの前に registry の取得と検証を全部済ませる。取得・検証の失敗では何も書かない
  const source = new RegistrySource(config.registry, cwd, options.fetch ?? ((url) => fetch(url)));
  const registry = await source.loadRegistry();
  const files: [dest: string, bytes: Buffer][] = [];
  for (const name of ["Core", "Default"]) {
    for (const file of findItem(registry, name).files) {
      assertSafeRelativePath(file.path);
      files.push([path.join(outputDir, file.path), await source.readFile(file.path)]);
    }
  }

  if (!existing) {
    await createFile(honeJsonPath, toJson(config), log, cwd);
  }
  for (const [dest, bytes] of files) {
    await createFile(dest, bytes, log, cwd);
  }
  const manifest = { version: 1, theme: `${output}/HoneTheme.tss`, fonts: [] };
  await createFile(path.join(outputDir, "hone.manifest.json"), toJson(manifest), log, cwd);

  log("Unity Editor に戻り、メニューの Hone > Sync を実行してください");
}
