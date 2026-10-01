import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { HoneError } from "./errors.js";
import { findItem, RegistrySource, type FetchLike } from "./registry.js";

export const DEFAULT_REGISTRY = "https://raw.githubusercontent.com/kaibutsu50/hone/main/";
const DEFAULT_OUTPUT = "Assets/Hone";

export interface InitOptions {
  cwd: string;
  registry?: string;
  fetch?: FetchLike;
  log?: (message: string) => void;
}

interface HoneConfig {
  registry: string;
  output: string;
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
  const config: HoneConfig = existing ?? {
    registry: options.registry ?? DEFAULT_REGISTRY,
    output: DEFAULT_OUTPUT,
  };
  const output = config.output.replace(/\\/g, "/").replace(/\/+$/, "");
  const outputDir = path.resolve(cwd, output);

  // 書き込みの前に必要なものを全部取得し、途中で失敗しても何も残らないようにする
  const source = new RegistrySource(config.registry, cwd, options.fetch ?? ((url) => fetch(url)));
  const registry = await source.loadRegistry();
  const files = new Map<string, Buffer>();
  for (const name of ["Core", "Default"]) {
    for (const file of findItem(registry, name).files) {
      const dest = path.resolve(outputDir, file.path);
      if (!dest.startsWith(outputDir + path.sep)) {
        throw new HoneError(`output の外へ出るパスは使えません: ${file.path}`);
      }
      if (!files.has(dest)) {
        files.set(dest, await source.readFile(file.path));
      }
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

async function isDirectory(p: string): Promise<boolean> {
  try {
    return (await stat(p)).isDirectory();
  } catch {
    return false;
  }
}

async function readHoneJson(file: string): Promise<HoneConfig | undefined> {
  let text: string;
  try {
    text = await readFile(file, "utf8");
  } catch {
    return undefined;
  }
  let json: Partial<HoneConfig>;
  try {
    json = JSON.parse(text);
  } catch {
    throw new HoneError("hone.json を JSON として読めませんでした");
  }
  if (typeof json.registry !== "string" || typeof json.output !== "string") {
    throw new HoneError("hone.json には registry と output（文字列）が必要です");
  }
  return { registry: json.registry, output: json.output };
}

function toJson(value: unknown): string {
  return JSON.stringify(value, null, 2) + "\n";
}

// 既にあるファイルは上書きしない（wx は存在すれば EEXIST で失敗する）
async function createFile(
  dest: string,
  data: string | Buffer,
  log: (message: string) => void,
  cwd: string,
): Promise<void> {
  const shown = path.relative(cwd, dest).split(path.sep).join("/");
  await mkdir(path.dirname(dest), { recursive: true });
  try {
    await writeFile(dest, data, { flag: "wx" });
    log(`作成: ${shown}`);
  } catch (e) {
    if ((e as NodeJS.ErrnoException).code !== "EEXIST") throw e;
    log(`既にあるためスキップ: ${shown}`);
  }
}
