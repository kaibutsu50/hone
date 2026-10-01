import { mkdtemp, mkdir, readFile, rm, stat, utimes, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { HoneError } from "../src/errors.js";
import { runInit } from "../src/init.js";
import { listFiles, makeUnityProject as makeProject, repoRoot } from "./helpers.js";

// テスト名の (A)〜(D) は Issue #20 の受け入れ条件の記号

let tmp: string;

beforeEach(async () => {
  tmp = await mkdtemp(path.join(tmpdir(), "hone-cli-"));
});

afterEach(async () => {
  await rm(tmp, { recursive: true, force: true, maxRetries: 3 });
});

const makeUnityProject = () => makeProject(tmp);

// registry.json と registry/ を持つ最小のローカル registry を作る
async function makeRegistry(
  name: string,
  items: { name: string; files: string[] }[],
  { writeFiles = true } = {},
): Promise<string> {
  const base = path.join(tmp, name);
  await mkdir(path.join(base, "registry"), { recursive: true });
  const json = {
    items: items.map((i) => ({
      name: i.name,
      type: "registry:lib",
      files: i.files.map((p) => ({ path: p, type: "registry:lib" })),
    })),
  };
  await writeFile(path.join(base, "registry.json"), JSON.stringify(json));
  for (const item of writeFiles ? items : []) {
    for (const p of item.files) {
      const dest = path.join(base, "registry", p);
      // "../x" のような path にも実ファイルを置く（読み込みの失敗で落ちる偽陽性を避ける）
      await mkdir(path.dirname(dest), { recursive: true });
      await writeFile(dest, "x");
    }
  }
  return base;
}

const silent = { log: () => {} };

describe("hone init", () => {
  it("(A)(D) Unity プロジェクトで init すると hone.json・Core・theme・manifest が作られる", async () => {
    const root = await makeUnityProject();

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await listFiles(root)).toEqual([
      "Assets/Hone/Core/Editor/Hone.Core.Editor.asmdef",
      "Assets/Hone/Core/Editor/HoneSync.cs",
      "Assets/Hone/Core/Runtime/BackStack.cs",
      "Assets/Hone/Core/Runtime/Core.uss",
      "Assets/Hone/Core/Runtime/FocusScope.cs",
      "Assets/Hone/Core/Runtime/Hone.Core.asmdef",
      "Assets/Hone/Core/Runtime/IDismissable.cs",
      "Assets/Hone/HoneTheme.tss",
      "Assets/Hone/Tokens.uss",
      "Assets/Hone/hone.manifest.json",
      "hone.json",
    ]);
    expect(JSON.parse(await readFile(path.join(root, "hone.json"), "utf8"))).toEqual({
      registry: repoRoot,
      output: "Assets/Hone",
    });
    expect(
      JSON.parse(await readFile(path.join(root, "Assets/Hone/hone.manifest.json"), "utf8")),
    ).toEqual({ version: 1, theme: "Assets/Hone/HoneTheme.tss", fonts: [] });
    // registry のファイルがそのまま（バイト単位で）コピーされる
    expect(await readFile(path.join(root, "Assets/Hone/Tokens.uss"))).toEqual(
      await readFile(path.join(repoRoot, "registry/Tokens.uss")),
    );
  });

  it("(A) 完了時に Hone > Sync の案内を表示する", async () => {
    const root = await makeUnityProject();
    const lines: string[] = [];

    await runInit({ cwd: root, registry: repoRoot, log: (m) => lines.push(m) });

    expect(lines.at(-1)).toContain("Hone > Sync");
  });

  it("(B) Unity プロジェクトでなければエラー終了し、ファイルを作らない", async () => {
    const root = path.join(tmp, "not-unity");
    await mkdir(root, { recursive: true });

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "Unity プロジェクトのルートで実行してください",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it("(B) Assets/ だけで ProjectSettings/ が無くてもエラー終了する", async () => {
    const root = path.join(tmp, "half");
    await mkdir(path.join(root, "Assets"), { recursive: true });

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "Unity プロジェクトのルートで実行してください",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it("(C) 2 回目の init は内容も更新時刻も変えない", async () => {
    const root = await makeUnityProject();
    await runInit({ cwd: root, registry: repoRoot, ...silent });
    // 時計の粒度に依存しないよう、過去の時刻に固定してから 2 回目を実行する
    const past = new Date("2000-01-01T00:00:00Z");
    for (const f of await listFiles(root)) await utimes(path.join(root, f), past, past);
    const snapshot = async () =>
      Promise.all(
        (await listFiles(root)).map(async (f) => {
          const p = path.join(root, f);
          return [f, (await stat(p)).mtimeMs, (await readFile(p)).toString("base64")] as const;
        }),
      );
    const before = await snapshot();

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await snapshot()).toEqual(before);
  });

  it("(C) 既存のファイルは上書きしない", async () => {
    const root = await makeUnityProject();
    await mkdir(path.join(root, "Assets/Hone"), { recursive: true });
    await writeFile(path.join(root, "Assets/Hone/Tokens.uss"), "/* 利用者が書き換えた */\n");

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await readFile(path.join(root, "Assets/Hone/Tokens.uss"), "utf8")).toBe(
      "/* 利用者が書き換えた */\n",
    );
  });

  it("既存の hone.json は書き換えず、その registry と output を使う", async () => {
    const root = await makeUnityProject();
    const honeJson = JSON.stringify({ registry: repoRoot, output: "Assets/Vendor/Hone" });
    await writeFile(path.join(root, "hone.json"), honeJson);

    await runInit({ cwd: root, registry: "https://example.invalid/", ...silent });

    expect(await readFile(path.join(root, "hone.json"), "utf8")).toBe(honeJson);
    expect(await listFiles(path.join(root, "Assets/Vendor/Hone"))).toContain("Tokens.uss");
    expect(
      JSON.parse(await readFile(path.join(root, "Assets/Vendor/Hone/hone.manifest.json"), "utf8"))
        .theme,
    ).toBe("Assets/Vendor/Hone/HoneTheme.tss");
  });

  it("file: URL のローカル registry も受け付ける", async () => {
    const root = await makeUnityProject();

    await runInit({ cwd: root, registry: pathToFileURL(repoRoot).href, ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/Tokens.uss");
  });

  it("URL の registry は <base>/registry.json と <base>/registry/<path> から fetch する", async () => {
    const root = await makeUnityProject();
    const requested: string[] = [];
    const fetchMock = async (url: string | URL): Promise<Response> => {
      const u = String(url);
      requested.push(u);
      const rel = u.replace("https://example.test/hone/", "");
      try {
        return new Response(await readFile(path.join(repoRoot, rel)));
      } catch {
        return new Response("not found", { status: 404 });
      }
    };

    await runInit({
      cwd: root,
      registry: "https://example.test/hone",
      fetch: fetchMock,
      ...silent,
    });

    expect(requested).toContain("https://example.test/hone/registry.json");
    expect(requested).toContain("https://example.test/hone/registry/Tokens.uss");
    expect(await listFiles(root)).toContain("Assets/Hone/Core/Runtime/Hone.Core.asmdef");
  });

  it("fetch が失敗したらエラー終了し、ファイルを作らない", async () => {
    const root = await makeUnityProject();
    const fetchMock = async (): Promise<Response> => new Response("nope", { status: 500 });

    await expect(
      runInit({ cwd: root, registry: "https://example.test/hone/", fetch: fetchMock, ...silent }),
    ).rejects.toThrow("500");
    expect(await listFiles(root)).toEqual([]);
  });

  it("途中のファイルの取得に失敗しても、hone.json を含め何も作らない", async () => {
    const root = await makeUnityProject();
    const fetchMock = async (url: string | URL): Promise<Response> => {
      const rel = String(url).replace("https://example.test/hone/", "");
      if (rel.endsWith("Tokens.uss")) return new Response("not found", { status: 404 });
      return new Response(await readFile(path.join(repoRoot, rel)));
    };

    await expect(
      runInit({ cwd: root, registry: "https://example.test/hone/", fetch: fetchMock, ...silent }),
    ).rejects.toThrow("404");
    expect(await listFiles(root)).toEqual([]);
  });

  it("fetch 自体が失敗（ネットワーク断）しても HoneError になり、何も作らない", async () => {
    const root = await makeUnityProject();
    const fetchMock = async (): Promise<Response> => {
      throw new TypeError("fetch failed");
    };

    await expect(
      runInit({ cwd: root, registry: "https://example.test/hone/", fetch: fetchMock, ...silent }),
    ).rejects.toBeInstanceOf(HoneError);
    expect(await listFiles(root)).toEqual([]);
  });

  it("項目名は大文字小文字を区別せずに照合する", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("lowercase-registry", [
      { name: "core", files: ["Core/Runtime/Hone.Core.asmdef"] },
      { name: "DEFAULT", files: ["Tokens.uss"] },
    ]);

    await runInit({ cwd: root, registry, ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/Core/Runtime/Hone.Core.asmdef");
    expect(await listFiles(root)).toContain("Assets/Hone/Tokens.uss");
  });

  it("Core はあるが Default が無い registry でも、何も作らない", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("core-only-registry", [
      { name: "Core", files: ["Core/Runtime/Hone.Core.asmdef"] },
    ]);

    await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("Default");
    expect(await listFiles(root)).toEqual([]);
  });

  it("registry に Core / Default が無ければエラー終了し、ファイルを作らない", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("empty-registry", []);

    await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("Core");
    expect(await listFiles(root)).toEqual([]);
  });

  it.each(["../escaped.txt", "..\\escaped.txt", "/abs.txt", "C:x.txt", "a/../../b.txt"])(
    "registry の files[].path が外へ出る・絶対・不正なら拒否する: %s",
    async (badPath) => {
      const root = await makeUnityProject();
      const registry = await makeRegistry(
        "evil-registry",
        [
          { name: "Core", files: [badPath] },
          { name: "Default", files: [] },
        ],
        { writeFiles: false },
      );

      await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("files[].path");
      expect(await listFiles(root)).toEqual([]);
    },
  );

  it.each([
    ["null", "null"],
    ["items が配列でない", JSON.stringify({ items: {} })],
    ["name が文字列でない", JSON.stringify({ items: [{ files: [] }] })],
    ["files が配列でない", JSON.stringify({ items: [{ name: "Core" }] })],
    ["JSON でない", "<html>"],
  ])("registry.json の形式が不正（%s）ならエラー終了し、何も作らない", async (_label, body) => {
    const root = await makeUnityProject();
    const registry = path.join(tmp, "broken-registry");
    await mkdir(registry, { recursive: true });
    await writeFile(path.join(registry, "registry.json"), body);

    await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("registry.json");
    expect(await listFiles(root)).toEqual([]);
  });

  it("2 回目の init は、利用者が書き換えた hone.manifest.json・hone.json・Core を保つ", async () => {
    const root = await makeUnityProject();
    await runInit({ cwd: root, registry: repoRoot, ...silent });
    const edited = [
      ["Assets/Hone/hone.manifest.json", '{ "fonts": ["ja"] }\n'],
      ["Assets/Hone/Core/Runtime/Core.uss", "/* 編集 */\n"],
    ] as const;
    for (const [f, text] of edited) await writeFile(path.join(root, f), text);
    const honeJson = JSON.stringify({ registry: repoRoot, output: "Assets/Hone", extra: 1 });
    await writeFile(path.join(root, "hone.json"), honeJson);

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    for (const [f, text] of edited) expect(await readFile(path.join(root, f), "utf8")).toBe(text);
    expect(await readFile(path.join(root, "hone.json"), "utf8")).toBe(honeJson);
  });

  it("既存の hone.json があるとき --registry が食い違えば、使わない旨を表示する", async () => {
    const root = await makeUnityProject();
    await writeFile(
      path.join(root, "hone.json"),
      JSON.stringify({ registry: repoRoot, output: "Assets/Hone" }),
    );
    const lines: string[] = [];

    await runInit({ cwd: root, registry: "https://example.invalid/", log: (m) => lines.push(m) });

    expect(lines[0]).toContain("--registry は使わず");
  });

  it.each([
    ["壊れた JSON", "{"],
    ["null", "null"],
    ["registry が文字列でない", JSON.stringify({ registry: 1, output: "Assets/Hone" })],
    ["output が文字列でない", JSON.stringify({ registry: "x" })],
  ])("hone.json が不正（%s）ならエラー終了し、何も作らない", async (_label, body) => {
    const root = await makeUnityProject();
    await writeFile(path.join(root, "hone.json"), body);

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow("hone.json");
    expect(await listFiles(root)).toEqual(["hone.json"]);
  });

  it("hone.json が読めない（ディレクトリ）ときは、既定値で進まずエラー終了する", async () => {
    const root = await makeUnityProject();
    await mkdir(path.join(root, "hone.json"));

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "hone.json を読めませんでした",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it.each(["..", "../Elsewhere", ".", "", "Assets/../.."])(
    "hone.json の output がプロジェクト内のサブディレクトリでなければ拒否する: %j",
    async (output) => {
      const root = await makeUnityProject();
      await writeFile(path.join(root, "hone.json"), JSON.stringify({ registry: repoRoot, output }));

      await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow("output");
      expect(await listFiles(root)).toEqual(["hone.json"]);
    },
  );

  it.each(["./Assets/Vendor/Hone", "Assets\\Vendor\\Hone\\", "Assets/Vendor/Hone/"])(
    "output の表記ゆれ（%s）は manifest の theme で / 区切りの相対パスにそろえる",
    async (output) => {
      const root = await makeUnityProject();
      await writeFile(path.join(root, "hone.json"), JSON.stringify({ registry: repoRoot, output }));

      await runInit({ cwd: root, registry: repoRoot, ...silent });

      const manifest = JSON.parse(
        await readFile(path.join(root, "Assets/Vendor/Hone/hone.manifest.json"), "utf8"),
      );
      expect(manifest.theme).toBe("Assets/Vendor/Hone/HoneTheme.tss");
    },
  );

  it("相対パスのローカル registry は cwd 基準で解決する", async () => {
    const root = await makeUnityProject();

    await runInit({ cwd: root, registry: path.relative(root, repoRoot), ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/Tokens.uss");
  });

  it("相対の file: URL は HoneError にする", async () => {
    const root = await makeUnityProject();

    await expect(runInit({ cwd: root, registry: "file:../hone", ...silent })).rejects.toThrow(
      "絶対パス",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it("書き込み先がディレクトリなら、スキップせずエラー終了する", async () => {
    const root = await makeUnityProject();
    await mkdir(path.join(root, "Assets/Hone/Tokens.uss"), { recursive: true });

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "書き込みに失敗しました",
    );
  });
});
