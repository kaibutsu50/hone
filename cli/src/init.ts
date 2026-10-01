import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { errorCode, HoneError, reason } from "./errors.js";
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
  if (existing && options.registry !== undefined && options.registry !== existing.registry) {
    log(
      `hone.json があるため --registry は使わず、hone.json の registry を使います: ${existing.registry}`,
    );
  }
  const config: HoneConfig = existing ?? {
    registry: options.registry ?? DEFAULT_REGISTRY,
    output: DEFAULT_OUTPUT,
  };

  // output はプロジェクト内のサブディレクトリに限る。manifest の theme は cwd からの相対で書く
  const outputDir = path.resolve(cwd, config.output);
  const relOutput = path.relative(cwd, outputDir);
  const outsideProject =
    relOutput === "" ||
    relOutput === ".." ||
    relOutput.startsWith(".." + path.sep) ||
    path.isAbsolute(relOutput);
  if (outsideProject) {
    throw new HoneError(
      `hone.json の output はプロジェクト内のサブディレクトリを指してください: ${config.output}`,
    );
  }
  const output = relOutput.split(path.sep).join("/");

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

// files[].path は registry/ からの相対で、読み取り側（<base>/registry/<path>）と
// 書き込み側（<output>/<path>）の両方で使う。どちらでも外へ出ないよう、字面で拒否する
function assertSafeRelativePath(p: string): void {
  if (p === "" || p.startsWith("/") || /[\\:]/.test(p) || p.split("/").includes("..")) {
    throw new HoneError(
      `registry の files[].path が不正です（相対パスで、.. と \\ と : は使えません）: ${p}`,
    );
  }
}

async function isDirectory(p: string): Promise<boolean> {
  try {
    return (await stat(p)).isDirectory();
  } catch (e) {
    if (errorCode(e) === "ENOENT" || errorCode(e) === "ENOTDIR") return false;
    throw new HoneError(`${p} を確認できませんでした（${reason(e)}）`, { cause: e });
  }
}

async function isFile(p: string): Promise<boolean> {
  try {
    return (await stat(p)).isFile();
  } catch {
    return false;
  }
}

async function readHoneJson(file: string): Promise<HoneConfig | undefined> {
  let text: string;
  try {
    text = await readFile(file, "utf8");
  } catch (e) {
    if (errorCode(e) === "ENOENT") return undefined;
    throw new HoneError(`hone.json を読めませんでした（${reason(e)}）`, { cause: e });
  }
  let json: Partial<HoneConfig> | null;
  try {
    json = JSON.parse(text);
  } catch (e) {
    throw new HoneError(`hone.json を JSON として読めませんでした（${reason(e)}）`, { cause: e });
  }
  if (typeof json?.registry !== "string" || typeof json.output !== "string") {
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
  try {
    await mkdir(path.dirname(dest), { recursive: true });
    await writeFile(dest, data, { flag: "wx" });
    log(`作成: ${shown}`);
  } catch (e) {
    if (errorCode(e) === "EEXIST" && (await isFile(dest))) {
      log(`既に存在するためスキップ: ${shown}`);
      return;
    }
    throw new HoneError(
      `書き込みに失敗しました（${reason(e)}）: ${shown}。ここまでに作成したファイルは残っています`,
      { cause: e },
    );
  }
}
