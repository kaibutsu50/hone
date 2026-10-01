import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { errorCode, HoneError, reason } from "./errors.js";

export interface HoneConfig {
  registry: string;
  output: string;
}

// output はプロジェクト内のサブディレクトリに限る。manifest の theme は cwd からの相対で書く
export function resolveOutput(
  cwd: string,
  config: HoneConfig,
): { outputDir: string; output: string } {
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
  return { outputDir, output: relOutput.split(path.sep).join("/") };
}

// files[].path は registry/ からの相対で、読み取り側（<base>/registry/<path>）と
// 書き込み側（<output>/<path>）の両方で使う。どちらでも外へ出ないよう、字面で拒否する
export function assertSafeRelativePath(p: string): void {
  if (p === "" || p.startsWith("/") || /[\\:]/.test(p) || p.split("/").includes("..")) {
    throw new HoneError(
      `registry の files[].path が不正です（相対パスで、.. と \\ と : は使えません）: ${p}`,
    );
  }
}

export async function isDirectory(p: string): Promise<boolean> {
  try {
    return (await stat(p)).isDirectory();
  } catch (e) {
    if (errorCode(e) === "ENOENT" || errorCode(e) === "ENOTDIR") return false;
    throw new HoneError(`${p} を確認できませんでした（${reason(e)}）`, { cause: e });
  }
}

export async function isFile(p: string): Promise<boolean> {
  try {
    return (await stat(p)).isFile();
  } catch (e) {
    if (errorCode(e) === "ENOENT" || errorCode(e) === "ENOTDIR") return false;
    throw new HoneError(`${p} を確認できませんでした（${reason(e)}）`, { cause: e });
  }
}

export async function readHoneJson(file: string): Promise<HoneConfig | undefined> {
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

export function toJson(value: unknown): string {
  return JSON.stringify(value, null, 2) + "\n";
}

// 既にあるファイルは上書きしない（wx は存在すれば EEXIST で失敗する）
export async function createFile(
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
