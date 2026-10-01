import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { homedir } from "node:os";
import path from "node:path";
import { unzipSync } from "fflate";
import { HoneError, reason } from "./errors.js";
import { FONT_SOURCES, type FontSource } from "./fonts.js";
import {
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
import type { FetchLike } from "./registry.js";

export const DEFAULT_FONT_CACHE = path.join(homedir(), ".cache", "hone", "fonts");

export interface AddFontOptions {
  cwd: string;
  langs: string[];
  fetch?: FetchLike;
  cacheDir?: string;
  log?: (message: string) => void;
}

interface FontBytes {
  regular: Buffer;
  bold: Buffer;
  license: Buffer;
}

export async function runAddFont(options: AddFontOptions): Promise<void> {
  const { cwd } = options;
  const log = options.log ?? console.log;
  const fetchFn = options.fetch ?? ((url) => fetch(url));
  const cacheDir = options.cacheDir ?? DEFAULT_FONT_CACHE;

  if (options.langs.length === 0) {
    throw new HoneError(`言語を指定してください。対応している lang: ${supportedLangs()}`);
  }
  const sources = [...new Set(options.langs.map(findSource))];

  const config = await readHoneJson(path.join(cwd, "hone.json"));
  if (!config) {
    throw new HoneError("hone.json がありません。先に `hone init` を実行してください");
  }
  const { outputDir, output } = resolveOutput(cwd, config);
  const manifestPath = path.join(outputDir, "hone.manifest.json");
  const manifest = parseManifest(await readInitFile(manifestPath, cwd), shownPath(cwd, manifestPath));

  // 書き込みの前に、取得を全部済ませる。どれかに失敗したら何も書かない。
  // 配置先が全部あれば取得しない（同じ lang の再実行でダウンロードしない）
  const plans: { source: FontSource; files: [dest: string, bytes: Buffer][] }[] = [];
  for (const source of sources) {
    const names = fileNames(source);
    const dests = [names.regular, names.bold, names.license].map((name) =>
      path.join(outputDir, "Fonts", source.dir, name),
    );
    for (const dest of dests) {
      if (await isDirectory(dest)) {
        throw new HoneError(`書き込み先がディレクトリです: ${shownPath(cwd, dest)}`);
      }
    }
    const installed = (await Promise.all(dests.map(isFile))).every(Boolean);
    const files: [string, Buffer][] = [];
    if (!installed) {
      const bytes = await loadFont(source, fetchFn, cacheDir, log, cwd);
      files.push([dests[0], bytes.regular], [dests[1], bytes.bold], [dests[2], bytes.license]);
    }
    plans.push({ source, files });
  }

  const added: unknown[] = [];
  for (const { source, files } of plans) {
    for (const [dest, bytes] of files) await createFile(dest, bytes, log, cwd);
    const registered = manifest.fonts.some((f) => (f as { lang?: unknown } | null)?.lang === source.lang);
    if (!registered) {
      const names = fileNames(source);
      added.push({
        lang: source.lang,
        family: source.family,
        files: [names.regular, names.bold].map((name) => `${output}/Fonts/${source.dir}/${name}`),
      });
    }
  }
  if (added.length > 0) {
    await overwrite(manifestPath, toJson({ ...manifest.json, fonts: [...manifest.fonts, ...added] }), log, cwd);
  }

  log("Unity Editor に戻り、メニューの Hone > Sync を実行してください");
}

function supportedLangs(): string {
  return FONT_SOURCES.map((s) => s.lang).join(", ");
}

// lang は大文字小文字を区別しない
function findSource(lang: string): FontSource {
  const source = FONT_SOURCES.find((s) => s.lang === lang.toLowerCase());
  if (!source) {
    throw new HoneError(`未対応の lang です: ${lang}。対応している lang: ${supportedLangs()}`);
  }
  return source;
}

function fileNames(source: FontSource): { regular: string; bold: string; license: string } {
  return {
    regular: `${source.dir}-Regular.otf`,
    bold: `${source.dir}-Bold.otf`,
    license: "LICENSE.txt",
  };
}

// キャッシュ（<cacheDir>/<タグ>/<dir>/）に 3 ファイルが揃っていればそれを使い、無ければ取得してキャッシュに置く。
// タグを固定しているので、キャッシュに期限は無い
async function loadFont(
  source: FontSource,
  fetchFn: FetchLike,
  cacheDir: string,
  log: (message: string) => void,
  cwd: string,
): Promise<FontBytes> {
  const names = fileNames(source);
  const dir = path.join(cacheDir, source.tag, source.dir);
  const cached = [names.regular, names.bold, names.license].map((name) => path.join(dir, name));
  if ((await Promise.all(cached.map(isFile))).every(Boolean)) {
    log(`キャッシュを使用: ${source.family}（${source.tag}）`);
    const [regular, bold, license] = await Promise.all(cached.map((file) => readFile(file)));
    return { regular, bold, license };
  }

  log(`取得: ${source.family}（${source.url}）`);
  const bytes = await download(source, fetchFn);
  const contents = [bytes.regular, bytes.bold, bytes.license];
  try {
    await mkdir(dir, { recursive: true });
    // 全部を一時ファイルに書いてから名前を付ける（途中で止まっても、揃っていないキャッシュは使われない）
    await Promise.all(cached.map((file, i) => writeFile(`${file}.tmp`, contents[i])));
    for (const file of cached) await rename(`${file}.tmp`, file);
  } catch (e) {
    await Promise.all(cached.map((file) => rm(`${file}.tmp`, { force: true })));
    log(`警告: キャッシュに保存できませんでした（${reason(e)}）: ${shownPath(cwd, dir)}`);
  }
  return bytes;
}

async function download(source: FontSource, fetchFn: FetchLike): Promise<FontBytes> {
  let archive: Uint8Array;
  try {
    const res = await fetchFn(source.url);
    if (!res.ok) {
      throw new HoneError(`フォントの取得に失敗しました（HTTP ${res.status}）: ${source.url}`);
    }
    archive = new Uint8Array(await res.arrayBuffer());
  } catch (e) {
    if (e instanceof HoneError) throw e;
    throw new HoneError(
      `フォントを取得できません（${reason(e)}）。ネットワークに接続するか、キャッシュのある環境で実行してください: ${source.url}`,
      { cause: e },
    );
  }

  const wanted = Object.values(source.entries);
  let unzipped: Record<string, Uint8Array>;
  try {
    unzipped = unzipSync(archive, { filter: (file) => wanted.includes(file.name) });
  } catch (e) {
    throw new HoneError(`取得した zip を展開できませんでした（${reason(e)}）: ${source.url}`, {
      cause: e,
    });
  }
  const pick = (name: string): Buffer => {
    const data = unzipped[name];
    if (!data) throw new HoneError(`取得した zip に ${name} がありません: ${source.url}`);
    return Buffer.from(data);
  };
  return {
    regular: pick(source.entries.regular),
    bold: pick(source.entries.bold),
    license: pick(source.entries.license),
  };
}
