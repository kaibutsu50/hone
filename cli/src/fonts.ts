// hone add font が取得するフォント。URL とタグは各リポジトリのリリースページで確かめて固定している。
// 配置先は <出力先>/Fonts/<dir>/ で、<dir>-Regular.otf、<dir>-Bold.otf、LICENSE.txt を置く
export interface FontSource {
  lang: string;
  family: string;
  dir: string;
  tag: string;
  url: string;
  // zip 内のパス
  entries: { regular: string; bold: string; license: string };
}

// notofonts/noto-cjk の言語別 Sans（zip の直下に <dir>-Regular.otf と LICENSE がある）
function cjk(lang: string, family: string, dir: string, asset: string): FontSource {
  const tag = "Sans2.004";
  return {
    lang,
    family,
    dir,
    tag,
    url: `https://github.com/notofonts/noto-cjk/releases/download/${tag}/${asset}`,
    entries: { regular: `${dir}-Regular.otf`, bold: `${dir}-Bold.otf`, license: "LICENSE" },
  };
}

// notofonts/<repo> の単体リリース（zip 内の <dir>/full/otf/ に otf、直下に OFL.txt がある）
function script(lang: string, family: string, dir: string, repo: string, version: string): FontSource {
  const tag = `${dir}-${version}`;
  return {
    lang,
    family,
    dir,
    tag,
    url: `https://github.com/notofonts/${repo}/releases/download/${tag}/${tag}.zip`,
    entries: {
      regular: `${dir}/full/otf/${dir}-Regular.otf`,
      bold: `${dir}/full/otf/${dir}-Bold.otf`,
      license: "OFL.txt",
    },
  };
}

export const FONT_SOURCES: FontSource[] = [
  cjk("ja", "Noto Sans JP", "NotoSansJP", "16_NotoSansJP.zip"),
  cjk("ko", "Noto Sans KR", "NotoSansKR", "17_NotoSansKR.zip"),
  cjk("zh-hans", "Noto Sans SC", "NotoSansSC", "18_NotoSansSC.zip"),
  cjk("zh-hant", "Noto Sans TC", "NotoSansTC", "19_NotoSansTC.zip"),
  script("ar", "Noto Sans Arabic", "NotoSansArabic", "arabic", "v2.013"),
  script("th", "Noto Sans Thai", "NotoSansThai", "thai", "v2.002"),
];
