import { readFile } from "node:fs/promises";
import path from "node:path";
import { HoneError, reason } from "./errors.js";
import { runAddFont } from "./font.js";
import {
  assertSafeRelativePath,
  createFile,
  isDirectory,
  isFile,
  overwrite,
  parseManifest,
  readHoneJson,
  readInitFile,
  resolveOutput,
  shownPath,
  toJson,
} from "./project.js";
import {
  findItem,
  RegistrySource,
  type FetchLike,
  type Registry,
  type RegistryItem,
} from "./registry.js";

// 引数なしの hone add の一覧と、hone.manifest.json の components に載せる種別。add 自体はどの項目名でも受け付ける
const COMPONENT_TYPES = ["registry:ui", "registry:block"];
const isComponent = (item: RegistryItem): boolean => COMPONENT_TYPES.includes(item.type);

export interface AddOptions {
  cwd: string;
  names: string[];
  fetch?: FetchLike;
  // add font のダウンロードのキャッシュ先。省略すると ~/.cache/hone/fonts
  cacheDir?: string;
  log?: (message: string) => void;
}

export async function runAdd(options: AddOptions): Promise<void> {
  const { cwd, names } = options;
  const log = options.log ?? console.log;

  // `add font <lang...>` は registry の項目ではない。registry の照合より前に分ける
  if (names[0]?.toLowerCase() === "font") {
    await runAddFont({ ...options, langs: names.slice(1) });
    return;
  }

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
  // 依頼されていない依存先は、ファイルが全部あればコピーも表示もしない（init 済みの Core、追加済みのプリミティブ）。
  // @import と components の確認は ui / block の依存先にも行う（前回の実行が途中で止まっていても、再実行で補える）
  const files: [dest: string, bytes: Buffer][] = [];
  const active: RegistryItem[] = [];
  for (const item of items) {
    const dests = item.files.map((f) => path.join(outputDir, f.path));
    for (const dest of dests) {
      if (await isDirectory(dest)) {
        throw new HoneError(`書き込み先がディレクトリです: ${shownPath(cwd, dest)}`);
      }
    }
    const installed = (await Promise.all(dests.map(isFile))).every(Boolean);
    if (requested.has(item) || !installed) {
      for (const [i, file] of item.files.entries()) {
        files.push([dests[i], await source.readFile(file.path)]);
      }
    }
    if (requested.has(item) || !installed || isComponent(item)) active.push(item);
  }

  const themePath = path.join(outputDir, "HoneTheme.tss");
  const theme = await readInitFile(themePath, cwd);
  const imports = active
    .flatMap((item) => item.files)
    .filter((f) => f.path.endsWith(".uss"))
    .map((f) => f.path);
  const newTheme = insertImports(theme, imports);

  const manifestPath = path.join(outputDir, "hone.manifest.json");
  const manifest = parseManifest(await readInitFile(manifestPath, cwd), shownPath(cwd, manifestPath));
  const newComponents = active
    .filter((i) => isComponent(i) && !manifest.components.includes(i.name))
    .map((i) => i.name);

  for (const [dest, bytes] of files) {
    await createFile(dest, bytes, log, cwd);
  }
  if (newTheme !== theme) await overwrite(themePath, newTheme, log, cwd);
  if (newComponents.length > 0) {
    const components = [...manifest.components, ...newComponents];
    await overwrite(manifestPath, toJson({ ...manifest.json, components }), log, cwd);
  }

  await warnMissingPackages(cwd, active, log);
  log("Unity Editor に戻るとコンパイルされます");
}

function listComponents(registry: Registry, log: (message: string) => void): void {
  const rows = registry.items.filter(isComponent);
  if (rows.length === 0) {
    log("registry に追加できるコンポーネントがありません");
    return;
  }
  for (const item of rows) {
    const kind = item.type.replace("registry:", "");
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

// theme の最後の @import 行の直後に、まだ無い @import url("<path>"); を挿入した全文を返す。既存の行は書き換えない。
// 同じ USS を指す @import が既にあれば（引用符や空白の違いを問わず）挿入しない。
// @import は :root などの規則より前に置く（CSS と同じ規則に合わせる。Unity の挙動は確かめていない）ので、末尾には足さない。
// 改行コードは既存の theme に合わせる（CRLF ならそれに従う）
function insertImports(theme: string, paths: string[]): string {
  // コメント内の @import を拾わないよう、位置を保ったままコメントを空白にした写しを調べる
  const comments = [...theme.matchAll(/\/\*[\s\S]*?\*\//g)].map((m) => ({
    start: m.index,
    end: m.index + m[0].length,
  }));
  let masked = theme;
  for (const { start, end } of comments) {
    const blank = masked.slice(start, end).replace(/[^\r\n]/g, " ");
    masked = masked.slice(0, start) + blank + masked.slice(end);
  }
  const existing = new Set<string>();
  let lastEnd = -1;
  for (const m of masked.matchAll(/^[ \t]*@import\b[^\r\n]*/gm)) {
    const url = /url\(\s*(["']?)(.*?)\1\s*\)/.exec(m[0]);
    if (url) existing.add(url[2]);
    lastEnd = m.index + m[0].length;
  }
  const fresh = [...new Set(paths)].filter((p) => !existing.has(p));
  if (fresh.length === 0) return theme;
  if (lastEnd < 0) {
    throw new HoneError(
      "HoneTheme.tss に @import 行がありません。挿入位置を決められないため何も書きません（HoneTheme.tss を registry のものに戻すか、@import を 1 行書いてください）",
    );
  }
  // 最後の @import の行末に複数行のコメントが跨っていれば、コメントの終わりの行末まで進める
  const straddling = comments.find((c) => c.start < lastEnd && lastEnd < c.end);
  if (straddling) {
    lastEnd = straddling.end + /^[^\r\n]*/.exec(theme.slice(straddling.end))![0].length;
  }
  const eol = theme.includes("\r\n") ? "\r\n" : "\n";
  const lines = fresh.map((p) => `${eol}@import url("${p}");`).join("");
  return theme.slice(0, lastEnd) + lines + theme.slice(lastEnd);
}

// UPM のパッケージは入れない。Packages/manifest.json の dependencies（直接の依存）に無ければ警告するだけ
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

  const file = path.join(cwd, "Packages/manifest.json");
  let installed: Set<string>;
  try {
    const manifest = JSON.parse(await readFile(file, "utf8")) as { dependencies?: object } | null;
    installed = new Set(Object.keys(manifest?.dependencies ?? {}));
  } catch (e) {
    log(
      `警告: ${shownPath(cwd, file)} を読めない（${reason(e)}）ため、必要なパッケージが入っているか確認できません: ${[...needed.keys()].join(", ")}`,
    );
    return;
  }
  for (const [pkg, users] of needed) {
    if (!installed.has(pkg)) {
      log(
        `警告: ${users.join(", ")} が使う UPM パッケージ ${pkg} が ${shownPath(cwd, file)} の dependencies に見つかりません。Package Manager でインストールしてください`,
      );
    }
  }
}
