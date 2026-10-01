import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { errorCode, HoneError, reason } from "./errors.js";
import {
  assertSafeRelativePath,
  createFile,
  isFile,
  readHoneJson,
  resolveOutput,
  toJson,
} from "./project.js";
import {
  findItem,
  RegistrySource,
  type FetchLike,
  type Registry,
  type RegistryItem,
} from "./registry.js";

// hone add の対象（hone add を引数なしで実行したときの一覧、hone.manifest.json の components に載るもの）
const COMPONENT_TYPES = ["registry:ui", "registry:block"];

export interface AddOptions {
  cwd: string;
  names: string[];
  fetch?: FetchLike;
  log?: (message: string) => void;
}

export async function runAdd(options: AddOptions): Promise<void> {
  const { cwd, names } = options;
  const log = options.log ?? console.log;

  const config = await readHoneJson(path.join(cwd, "hone.json"));
  if (!config) {
    throw new HoneError("hone.json がありません。先に `hone init` を実行してください");
  }
  const { outputDir } = resolveOutput(cwd, config);

  const source = new RegistrySource(config.registry, cwd, options.fetch ?? ((url) => fetch(url)));
  const registry = await source.loadRegistry();
  if (names.length === 0) {
    listComponents(registry, log);
    return;
  }

  // 書き込みの前に、解決・取得・検証を全部済ませる。どれかに失敗したら何も書かない
  const requested = new Set(names.map((name) => findItem(registry, name)));
  const items = resolveItems(registry, requested);
  for (const item of items) {
    for (const file of item.files) assertSafeRelativePath(file.path);
  }
  // 依頼されていない依存先は、ファイルが全部あれば何もしない（init 済みの Core、追加済みのプリミティブ）
  const toAdd: RegistryItem[] = [];
  for (const item of items) {
    const installed = (
      await Promise.all(item.files.map((f) => isFile(path.join(outputDir, f.path))))
    ).every(Boolean);
    if (requested.has(item) || !installed) toAdd.push(item);
  }

  const files: [dest: string, bytes: Buffer][] = [];
  for (const item of toAdd) {
    for (const file of item.files) {
      files.push([path.join(outputDir, file.path), await source.readFile(file.path)]);
    }
  }

  const themePath = path.join(outputDir, "HoneTheme.tss");
  const theme = await readInitFile(themePath, cwd);
  const imports = toAdd
    .flatMap((item) => item.files)
    .filter((f) => f.path.endsWith(".uss"))
    .map((f) => `@import url("${f.path}");`);
  const newTheme = insertImports(theme, imports);

  const manifestPath = path.join(outputDir, "hone.manifest.json");
  const manifest = parseManifest(await readInitFile(manifestPath, cwd), shownPath(cwd, manifestPath));
  const newComponents = toAdd
    .filter((i) => COMPONENT_TYPES.includes(i.type ?? "") && !manifest.components.includes(i.name))
    .map((i) => i.name);

  for (const [dest, bytes] of files) {
    await createFile(dest, bytes, log, cwd);
  }
  if (newTheme !== theme) await overwrite(themePath, newTheme, log, cwd);
  if (newComponents.length > 0) {
    const components = [...manifest.components, ...newComponents];
    await overwrite(manifestPath, toJson({ ...manifest.json, components }), log, cwd);
  }

  await warnMissingPackages(cwd, toAdd, log);
  log("Unity Editor に戻るとコンパイルされます");
}

function listComponents(registry: Registry, log: (message: string) => void): void {
  const rows = registry.items.filter((i) => COMPONENT_TYPES.includes(i.type ?? ""));
  if (rows.length === 0) {
    log("registry に追加できるコンポーネントがありません");
    return;
  }
  for (const item of rows) {
    const kind = item.type?.replace("registry:", "");
    log(`${item.name} (${kind})${item.description ? ` ${item.description}` : ""}`);
  }
}

// 依頼された項目を、registryDependencies を先にした順で並べる（重複と循環は 1 回にまとめる）
function resolveItems(registry: Registry, requested: Set<RegistryItem>): RegistryItem[] {
  const ordered: RegistryItem[] = [];
  const seen = new Set<RegistryItem>();
  const visit = (item: RegistryItem): void => {
    if (seen.has(item)) return;
    seen.add(item);
    for (const dep of item.registryDependencies ?? []) visit(findItem(registry, dep));
    ordered.push(item);
  };
  requested.forEach(visit);
  return ordered;
}

function shownPath(cwd: string, file: string): string {
  return path.relative(cwd, file).split(path.sep).join("/");
}

// init が作るファイル。無ければ init が済んでいない
async function readInitFile(file: string, cwd: string): Promise<string> {
  try {
    return await readFile(file, "utf8");
  } catch (e) {
    const shown = shownPath(cwd, file);
    if (errorCode(e) === "ENOENT") {
      throw new HoneError(`${shown} がありません。先に \`hone init\` を実行してください`, {
        cause: e,
      });
    }
    throw new HoneError(`${shown} を読めませんでした（${reason(e)}）`, { cause: e });
  }
}

function parseManifest(
  text: string,
  shown: string,
): { json: Record<string, unknown>; components: string[] } {
  let json: unknown;
  try {
    json = JSON.parse(text);
  } catch (e) {
    throw new HoneError(`${shown} を JSON として読めませんでした（${reason(e)}）`, { cause: e });
  }
  if (typeof json !== "object" || json === null || Array.isArray(json)) {
    throw new HoneError(`${shown} は JSON のオブジェクトである必要があります`);
  }
  const components = (json as { components?: unknown }).components ?? [];
  if (!Array.isArray(components) || !components.every((c) => typeof c === "string")) {
    throw new HoneError(`${shown} の components は文字列の配列である必要があります`);
  }
  return { json: json as Record<string, unknown>, components };
}

// theme の最後の @import 行の直後に、まだ無い行を挿入した全文を返す。既存の行は書き換えない。
// @import は :root などの規則より前に置く必要があるので、末尾には足さない。
// 改行コードは既存の theme に合わせる（Windows の作業ツリーでは CRLF になる）
function insertImports(theme: string, lines: string[]): string {
  // コメント内の @import を拾わないよう、位置を保ったままコメントを空白にした写しを調べる
  const masked = theme.replace(/\/\*[\s\S]*?\*\//g, (c) => c.replace(/[^\r\n]/g, " "));
  const existing = new Set<string>();
  let lastEnd = -1;
  for (const m of masked.matchAll(/^[ \t]*@import\b[^\r\n]*/gm)) {
    existing.add(m[0].trim());
    lastEnd = m.index + m[0].length;
  }
  const fresh = [...new Set(lines)].filter((l) => !existing.has(l));
  if (fresh.length === 0) return theme;
  if (lastEnd < 0) {
    throw new HoneError(
      "HoneTheme.tss に @import 行がありません。挿入位置を決められないため何も書きません（HoneTheme.tss を registry のものに戻すか、@import を 1 行書いてください）",
    );
  }
  const eol = theme.includes("\r\n") ? "\r\n" : "\n";
  return theme.slice(0, lastEnd) + fresh.map((l) => eol + l).join("") + theme.slice(lastEnd);
}

// 既存ファイルの更新（theme と manifest。内容が変わるときだけ呼ぶ）
async function overwrite(
  file: string,
  text: string,
  log: (message: string) => void,
  cwd: string,
): Promise<void> {
  const shown = shownPath(cwd, file);
  try {
    await writeFile(file, text);
  } catch (e) {
    throw new HoneError(
      `書き込みに失敗しました（${reason(e)}）: ${shown}。ここまでに作成したファイルは残っています`,
      { cause: e },
    );
  }
  log(`更新: ${shown}`);
}

// UPM のパッケージは入れない。Packages/manifest.json に無ければ警告するだけ
async function warnMissingPackages(
  cwd: string,
  items: RegistryItem[],
  log: (message: string) => void,
): Promise<void> {
  const needed = new Map<string, string[]>();
  for (const item of items) {
    for (const pkg of item.dependencies ?? []) {
      needed.set(pkg, [...(needed.get(pkg) ?? []), item.name]);
    }
  }
  if (needed.size === 0) return;

  let installed: Set<string> | undefined;
  try {
    const manifest = JSON.parse(await readFile(path.join(cwd, "Packages/manifest.json"), "utf8"));
    installed = new Set(Object.keys(manifest.dependencies));
  } catch {
    installed = undefined;
  }
  if (!installed) {
    log(
      `警告: Packages/manifest.json を読めないため、必要なパッケージが入っているか確認できません: ${[...needed.keys()].join(", ")}`,
    );
    return;
  }
  for (const [pkg, users] of needed) {
    if (!installed.has(pkg)) {
      log(
        `警告: ${users.join(", ")} には UPM パッケージ ${pkg} が必要ですが、Packages/manifest.json にありません。Package Manager でインストールしてください`,
      );
    }
  }
}
