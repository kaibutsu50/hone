import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { homedir } from "node:os";
import path from "node:path";
import { unzipSync } from "fflate";
import { errorCode, HoneError, reason } from "./errors.js";
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

// 使うときに評価する（HOME が引けない環境でも、font 以外のコマンドは動くように）
export const defaultFontCache = (): string => path.join(homedir(), ".cache", "hone", "fonts");

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
  const cacheDir = options.cacheDir ?? defaultFontCache();

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
  const readManifest = async () =>
    parseManifest(await readInitFile(manifestPath, cwd), shownPath(cwd, manifestPath));
  await readManifest(); // 取得の前に、manifest が読めることを確かめる

  // プロジェクトへの書き込みの前に、取得（とキャッシュへの保存）を全部済ませる。どれかに失敗したらプロジェクトには何も書かない。
  // 配置先に 3 ファイルが名前で揃っていれば取得しない（同じ lang の再実行でダウンロードしない。内容は比べない）
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
      const bytes = await loadFont(source, fetchFn, cacheDir, log);
      files.push([dests[0], bytes.regular], [dests[1], bytes.bold], [dests[2], bytes.license]);
    }
    plans.push({ source, files });
  }

  // 取得には時間がかかるので、書く直前に読み直す（その間に add や Sync が更新した内容を消さない）
  const manifest = await readManifest();
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
): Promise<FontBytes> {
  const names = fileNames(source);
  const dir = path.join(cacheDir, source.tag, source.dir);
  const cached = [names.regular, names.bold, names.license].map((name) => path.join(dir, name));
  const hit = await readCache(cached, dir, log);
  if (hit) {
    log(`キャッシュを使用: ${source.family}（${source.tag}）`);
    return { regular: hit[0], bold: hit[1], license: hit[2] };
  }

  log(`取得: ${source.family}（${source.url}）`);
  const bytes = await download(source, fetchFn);
  const contents = [bytes.regular, bytes.bold, bytes.license];
  const tmps = cached.map((file) => `${file}.${process.pid}.tmp`);
  try {
    await mkdir(dir, { recursive: true });
    // 全部を一時ファイルに書いてから名前を付ける（途中で止まっても、揃っていないキャッシュは使われない）
    const written = await Promise.allSettled(tmps.map((tmp, i) => writeFile(tmp, contents[i])));
    const failed = written.find((r) => r.status === "rejected");
    if (failed) throw failed.reason;
    for (const [i, file] of cached.entries()) await rename(tmps[i], file);
  } catch (e) {
    await Promise.all(tmps.map((tmp) => rm(tmp, { force: true }).catch(() => {})));
    log(`警告: キャッシュに保存できませんでした（${reason(e)}）: ${dir}`);
  }
  return bytes;
}

// 3 ファイルが揃っていて空でなければ、その内容。無い・欠けている・空のときは undefined（取得し直す）。
// 読めないとき（権限など）は、警告してキャッシュを使わない
async function readCache(
  files: string[],
  dir: string,
  log: (message: string) => void,
): Promise<Buffer[] | undefined> {
  try {
    const bytes = await Promise.all(files.map((file) => readFile(file)));
    return bytes.every((b) => b.length > 0) ? bytes : undefined;
  } catch (e) {
    if (errorCode(e) !== "ENOENT" && errorCode(e) !== "ENOTDIR") {
      log(`警告: キャッシュを読めませんでした（${reason(e)}）。取得し直します: ${dir}`);
    }
    return undefined;
  }
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
    // fetch の失敗は TypeError("fetch failed") で、本当の原因（ENOTFOUND など）は cause にある
    const detail = e instanceof Error && e.cause !== undefined ? `${reason(e)}: ${reason(e.cause)}` : reason(e);
    throw new HoneError(
      `フォントを取得できません（${detail}）。ネットワークに接続するか、このタグ（${source.tag}）のキャッシュがあるマシンで実行してください: ${source.url}`,
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
    if (data.length === 0) throw new HoneError(`取得した zip の ${name} が空です: ${source.url}`);
    return Buffer.from(data);
  };
  return {
    regular: pick(source.entries.regular),
    bold: pick(source.entries.bold),
    license: pick(source.entries.license),
  };
}
