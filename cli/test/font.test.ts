import { mkdtemp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { zipSync } from "fflate";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { runAdd } from "../src/add.js";
import { HoneError } from "../src/errors.js";
import { FONT_SOURCES, type FontSource } from "../src/fonts.js";
import { runInit } from "../src/init.js";
import { listFiles, makeUnityProject, repoRoot } from "./helpers.js";

// テスト名の (A)〜(E) は Issue #22 の受け入れ条件の記号

let tmp: string;
let cacheDir: string;

beforeEach(async () => {
  tmp = await mkdtemp(path.join(tmpdir(), "hone-cli-font-"));
  cacheDir = path.join(tmp, "cache");
});

afterEach(async () => {
  await rm(tmp, { recursive: true, force: true, maxRetries: 3 });
});

const silent = { log: () => {} };
const enc = (text: string) => new TextEncoder().encode(text);
const source = (lang: string): FontSource => FONT_SOURCES.find((s) => s.lang === lang)!;

// 実物と同じ構造の小さな zip（必要な 3 ファイルと、取り出されないはずのファイル）
function makeZip(s: FontSource): Uint8Array {
  return zipSync({
    [s.entries.regular]: enc(`${s.dir}:regular`),
    [s.entries.bold]: enc(`${s.dir}:bold`),
    [s.entries.license]: enc(`${s.dir}:license`),
    [s.entries.regular.replace("Regular", "Medium")]: enc("medium"),
    "README.txt": enc("readme"),
  });
}

// URL ごとに zip を返す fetch。呼ばれた URL を記録する
function makeFetch() {
  const calls: string[] = [];
  const fetchMock = async (url: string | URL): Promise<Response> => {
    calls.push(String(url));
    const s = FONT_SOURCES.find((x) => x.url === String(url));
    if (!s) return new Response("not found", { status: 404 });
    return new Response(makeZip(s));
  };
  return { calls, fetchMock };
}

async function makeInitializedProject(): Promise<string> {
  const root = await makeUnityProject(tmp);
  await runInit({ cwd: root, registry: repoRoot, ...silent });
  return root;
}

const read = (root: string, file: string) => readFile(path.join(root, file), "utf8");
const readManifest = async (root: string) =>
  JSON.parse(await read(root, "Assets/Hone/hone.manifest.json"));

const addFont = (root: string, langs: string[], fetchMock: (url: string | URL) => Promise<Response>) =>
  runAdd({ cwd: root, names: ["font", ...langs], fetch: fetchMock, cacheDir, ...silent });

describe("hone add font", () => {
  it.each(FONT_SOURCES.map((s) => [s.lang, s] as const))(
    "(A) %s: Regular と Bold の otf と LICENSE.txt を置き、fonts に 1 項目追記する",
    async (_lang, s) => {
      const root = await makeInitializedProject();
      const { calls, fetchMock } = makeFetch();

      await addFont(root, [s.lang], fetchMock);

      const dir = `Assets/Hone/Fonts/${s.dir}`;
      expect(await listFiles(path.join(root, dir))).toEqual([
        "LICENSE.txt",
        `${s.dir}-Bold.otf`,
        `${s.dir}-Regular.otf`,
      ]);
      expect(await read(root, `${dir}/${s.dir}-Regular.otf`)).toBe(`${s.dir}:regular`);
      expect(await read(root, `${dir}/${s.dir}-Bold.otf`)).toBe(`${s.dir}:bold`);
      expect(await read(root, `${dir}/LICENSE.txt`)).toBe(`${s.dir}:license`);
      expect((await readManifest(root)).fonts).toEqual([
        {
          lang: s.lang,
          family: s.family,
          files: [`${dir}/${s.dir}-Regular.otf`, `${dir}/${s.dir}-Bold.otf`],
        },
      ]);
      expect(calls).toEqual([s.url]);
    },
  );

  it("(A) 既存の manifest の他の項目（components、fonts の既存項目）を変えない", async () => {
    const root = await makeInitializedProject();
    const manifestPath = path.join(root, "Assets/Hone/hone.manifest.json");
    const manifest = await readManifest(root);
    const other = { lang: "xx", family: "Other", files: ["a.otf"] };
    await writeFile(manifestPath, JSON.stringify({ ...manifest, components: ["Button"], fonts: [other] }));

    await addFont(root, ["ja"], makeFetch().fetchMock);

    const after = await readManifest(root);
    expect(after.components).toEqual(["Button"]);
    expect(after.version).toBe(manifest.version);
    expect(after.theme).toBe(manifest.theme);
    expect(after.fonts.map((f: { lang: string }) => f.lang)).toEqual(["xx", "ja"]);
    expect(after.fonts[0]).toEqual(other);
  });

  it("(A) hone.json の output を変えると、置き場所と manifest のパスもそれに従う", async () => {
    const root = await makeUnityProject(tmp);
    await runInit({ cwd: root, registry: repoRoot, ...silent });
    const config = JSON.parse(await read(root, "hone.json"));
    await rm(path.join(root, "Assets/Hone"), { recursive: true });
    await writeFile(path.join(root, "hone.json"), JSON.stringify({ ...config, output: "Assets/Custom/Hone" }));
    await runInit({ cwd: root, ...silent });

    await addFont(root, ["ja"], makeFetch().fetchMock);

    const manifest = JSON.parse(await read(root, "Assets/Custom/Hone/hone.manifest.json"));
    expect(manifest.fonts[0].files).toEqual([
      "Assets/Custom/Hone/Fonts/NotoSansJP/NotoSansJP-Regular.otf",
      "Assets/Custom/Hone/Fonts/NotoSansJP/NotoSansJP-Bold.otf",
    ]);
    expect(await listFiles(path.join(root, "Assets/Custom/Hone/Fonts/NotoSansJP"))).toHaveLength(3);
  });

  it("(B) 複数の lang を一度に渡すと、全部置かれ、fonts が 2 項目になる", async () => {
    const root = await makeInitializedProject();
    const { calls, fetchMock } = makeFetch();

    await addFont(root, ["ja", "ko"], fetchMock);

    expect((await readManifest(root)).fonts.map((f: { lang: string }) => f.lang)).toEqual(["ja", "ko"]);
    expect(await listFiles(path.join(root, "Assets/Hone/Fonts"))).toEqual([
      "NotoSansJP/LICENSE.txt",
      "NotoSansJP/NotoSansJP-Bold.otf",
      "NotoSansJP/NotoSansJP-Regular.otf",
      "NotoSansKR/LICENSE.txt",
      "NotoSansKR/NotoSansKR-Bold.otf",
      "NotoSansKR/NotoSansKR-Regular.otf",
    ]);
    expect(calls).toEqual([source("ja").url, source("ko").url]);
  });

  it("(B) 同じ lang の重複や大文字小文字の違いは 1 つにまとめる", async () => {
    const root = await makeInitializedProject();
    const { calls, fetchMock } = makeFetch();

    await addFont(root, ["ja", "JA", "ja"], fetchMock);

    expect((await readManifest(root)).fonts).toHaveLength(1);
    expect(calls).toEqual([source("ja").url]);
  });

  it("(C) 同じ lang を 2 回実行しても、再ダウンロードも fonts の重複も、ファイルの書き換えも起きない", async () => {
    const root = await makeInitializedProject();
    const first = makeFetch();
    await addFont(root, ["ja"], first.fetchMock);
    const target = path.join(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Regular.otf");
    const mtime = (await stat(target)).mtimeMs;
    const manifestBefore = await read(root, "Assets/Hone/hone.manifest.json");
    // キャッシュがあっても取得しないことを確かめるため、キャッシュを消す
    await rm(cacheDir, { recursive: true });

    const second = makeFetch();
    const logs: string[] = [];
    await runAdd({ cwd: root, names: ["font", "ja"], fetch: second.fetchMock, cacheDir, log: (m) => logs.push(m) });

    expect(second.calls).toEqual([]);
    expect((await readManifest(root)).fonts).toHaveLength(1);
    expect(await read(root, "Assets/Hone/hone.manifest.json")).toBe(manifestBefore);
    expect((await stat(target)).mtimeMs).toBe(mtime);
    expect(logs.at(-1)).toContain("Hone > Sync");
  });

  it("(C) ファイルはあるが manifest に無いときは、取得せず manifest にだけ追記する", async () => {
    const root = await makeInitializedProject();
    await addFont(root, ["ja"], makeFetch().fetchMock);
    const manifest = await readManifest(root);
    await writeFile(
      path.join(root, "Assets/Hone/hone.manifest.json"),
      JSON.stringify({ ...manifest, fonts: [] }),
    );
    await rm(cacheDir, { recursive: true });

    const { calls, fetchMock } = makeFetch();
    await addFont(root, ["ja"], fetchMock);

    expect(calls).toEqual([]);
    expect((await readManifest(root)).fonts.map((f: { lang: string }) => f.lang)).toEqual(["ja"]);
  });

  it("(C) 一部のファイルだけ欠けているときは、あるファイルを上書きせず、欠けたものだけ置く", async () => {
    const root = await makeInitializedProject();
    await addFont(root, ["ja"], makeFetch().fetchMock);
    const regular = path.join(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Regular.otf");
    await writeFile(regular, "edited by user");
    await rm(path.join(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Bold.otf"));

    await addFont(root, ["ja"], makeFetch().fetchMock);

    expect(await readFile(regular, "utf8")).toBe("edited by user");
    expect(await read(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Bold.otf")).toBe("NotoSansJP:bold");
    expect((await readManifest(root)).fonts).toHaveLength(1);
  });

  it("(D) 未対応の lang はエラーにして対応一覧を表示し、何も取得も書き込みもしない", async () => {
    const root = await makeInitializedProject();
    const manifestBefore = await read(root, "Assets/Hone/hone.manifest.json");
    const { calls, fetchMock } = makeFetch();

    const err = await addFont(root, ["ja", "fr"], fetchMock).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(HoneError);
    expect((err as Error).message).toContain("fr");
    for (const lang of ["ja", "ko", "zh-hans", "zh-hant", "ar", "th"]) {
      expect((err as Error).message).toContain(lang);
    }
    expect(calls).toEqual([]);
    expect(await read(root, "Assets/Hone/hone.manifest.json")).toBe(manifestBefore);
    await expect(stat(path.join(root, "Assets/Hone/Fonts"))).rejects.toThrow();
  });

  it("(D) lang を渡さないときもエラーにして対応一覧を表示する", async () => {
    const root = await makeInitializedProject();

    await expect(addFont(root, [], makeFetch().fetchMock)).rejects.toThrow(/lang.*ja, ko, zh-hans, zh-hant, ar, th/);
  });

  it("(E) キャッシュがあれば、ネットワークが無くても (A) が成功する", async () => {
    const root = await makeInitializedProject();
    await addFont(root, ["ja"], makeFetch().fetchMock);
    await rm(path.join(root, "Assets/Hone/Fonts"), { recursive: true });
    const manifest = await readManifest(root);
    await writeFile(path.join(root, "Assets/Hone/hone.manifest.json"), JSON.stringify({ ...manifest, fonts: [] }));
    const offline = async (): Promise<Response> => {
      throw new TypeError("fetch failed");
    };

    await addFont(root, ["ja"], offline);

    expect(await read(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Regular.otf")).toBe("NotoSansJP:regular");
    expect(await read(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Bold.otf")).toBe("NotoSansJP:bold");
    expect(await read(root, "Assets/Hone/Fonts/NotoSansJP/LICENSE.txt")).toBe("NotoSansJP:license");
    expect((await readManifest(root)).fonts).toHaveLength(1);
  });

  it("(E) キャッシュも無くネットワークも無いときは、エラー終了して何も書かない", async () => {
    const root = await makeInitializedProject();
    const manifestBefore = await read(root, "Assets/Hone/hone.manifest.json");
    const offline = async (): Promise<Response> => {
      throw new TypeError("fetch failed");
    };

    const err = await addFont(root, ["ja"], offline).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(HoneError);
    expect((err as Error).message).toContain("fetch failed");
    expect((err as Error).message).toContain(source("ja").url);
    expect(await read(root, "Assets/Hone/hone.manifest.json")).toBe(manifestBefore);
    await expect(stat(path.join(root, "Assets/Hone/Fonts"))).rejects.toThrow();
  });

  it("複数の lang の途中で取得に失敗したら、先に取得できた分も含めて何も書かない", async () => {
    const root = await makeInitializedProject();
    const manifestBefore = await read(root, "Assets/Hone/hone.manifest.json");
    const { fetchMock } = makeFetch();
    const failKo = async (url: string | URL): Promise<Response> =>
      String(url) === source("ko").url ? new Response("", { status: 503 }) : fetchMock(url);

    await expect(addFont(root, ["ja", "ko"], failKo)).rejects.toThrow(/HTTP 503/);

    expect(await read(root, "Assets/Hone/hone.manifest.json")).toBe(manifestBefore);
    await expect(stat(path.join(root, "Assets/Hone/Fonts"))).rejects.toThrow();
  });

  it("zip に必要なファイルが無ければ、どのファイルが無いかを示してエラーにする", async () => {
    const root = await makeInitializedProject();
    const s = source("ja");
    const broken = async (): Promise<Response> => new Response(zipSync({ [s.entries.regular]: enc("x") }));

    await expect(addFont(root, ["ja"], broken)).rejects.toThrow(s.entries.bold);
  });

  it("zip でない応答はエラーにする", async () => {
    const root = await makeInitializedProject();
    const html = async (): Promise<Response> => new Response("<html>not a zip</html>");

    await expect(addFont(root, ["ja"], html)).rejects.toThrow(HoneError);
  });

  it("registry を読まない（registry が取得できなくても動く）", async () => {
    const root = await makeInitializedProject();
    const config = JSON.parse(await read(root, "hone.json"));
    await writeFile(path.join(root, "hone.json"), JSON.stringify({ ...config, registry: path.join(tmp, "missing") }));

    await addFont(root, ["ja"], makeFetch().fetchMock);

    expect((await readManifest(root)).fonts).toHaveLength(1);
  });

  it("init 前（hone.json がない）はエラーにする", async () => {
    const root = await makeUnityProject(tmp);

    await expect(addFont(root, ["ja"], makeFetch().fetchMock)).rejects.toThrow("hone init");
  });

  it("配置先がディレクトリのときは、何も書かずにエラーにする", async () => {
    const root = await makeInitializedProject();
    await mkdir(path.join(root, "Assets/Hone/Fonts/NotoSansJP/LICENSE.txt"), { recursive: true });

    await expect(addFont(root, ["ja"], makeFetch().fetchMock)).rejects.toThrow("ディレクトリ");
    await expect(stat(path.join(root, "Assets/Hone/Fonts/NotoSansJP/NotoSansJP-Regular.otf"))).rejects.toThrow();
  });

  it("キャッシュは <タグ>/<dir>/ に 3 ファイルだけ置き、一時ファイルを残さない", async () => {
    const root = await makeInitializedProject();
    const s = source("ar");

    await addFont(root, ["ar"], makeFetch().fetchMock);

    expect(await listFiles(cacheDir)).toEqual([
      `${s.tag}/${s.dir}/LICENSE.txt`,
      `${s.tag}/${s.dir}/${s.dir}-Bold.otf`,
      `${s.tag}/${s.dir}/${s.dir}-Regular.otf`,
    ]);
  });
});

describe("FONT_SOURCES", () => {
  it("lang は一意で、URL は固定タグの GitHub リリースを指す", () => {
    expect(new Set(FONT_SOURCES.map((s) => s.lang)).size).toBe(FONT_SOURCES.length);
    for (const s of FONT_SOURCES) {
      expect(s.url).toMatch(/^https:\/\/github\.com\/notofonts\/[a-z-]+\/releases\/download\/[^/]+\/[^/]+\.zip$/);
      expect(s.url).toContain(`/${s.tag}/`);
      expect(s.tag).not.toMatch(/latest/i);
    }
  });
});
