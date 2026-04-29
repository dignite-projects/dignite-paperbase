import { pipeline } from '@xenova/transformers';
import { writeFileSync } from 'fs';

// Desensitized Japanese corpus — contracts, invoices, certificates, prose
const chunks = [
  // ── Contracts ─────────────────────────────────────────────────────────────
  {
    id: '11111111-0001-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '業務委託契約書\n契約番号： CNT-2025-0001\n甲：株式会社サンプル（以下「甲」という）\n乙：山田商事株式会社（以下「乙」という）\n甲乙間において、以下のとおり業務委託契約を締結する。',
  },
  {
    id: '11111111-0002-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第1条（委託業務）\n甲は乙に対し、システム開発業務を委託する。契約番号 CNT-2025-0001 に基づき、乙は誠実に業務を遂行するものとする。',
  },
  {
    id: '11111111-0003-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第2条（委託料）\n甲は乙に対し、委託料として月額金50万円（消費税別）を支払う。支払期日は毎月末日とする。契約番号 CNT-2025-0001 。',
  },
  {
    id: '11111111-0004-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第3条（契約期間）\n本契約の有効期間は2025年5月1日から2026年4月30日までとする。期間満了の1ヶ月前までに解約の申し出がない場合は自動更新する。契約番号 CNT-2025-0001 。',
  },
  {
    id: '11111111-0005-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '業務委託契約書\n契約番号： CNT-2025-0002\n甲：有限会社テスト工業（以下「甲」という）\n乙：鈴木技研株式会社（以下「乙」という）\n甲乙間において、以下のとおり業務委託契約を締結する。',
  },
  {
    id: '11111111-0006-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第1条（委託業務）\n甲は乙に対し、製品検査業務を委託する。契約番号 CNT-2025-0002 に基づき、乙は週次報告書を提出する義務を負う。',
  },
  {
    id: '11111111-0007-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第2条（委託料）\n甲は乙に対し、委託料として月額金80万円（消費税別）を支払う。支払期日は毎月20日とする。契約番号 CNT-2025-0002 。',
  },
  {
    id: '11111111-0008-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第3条（秘密保持）\n乙は、本契約の履行により知り得た甲の秘密情報を第三者に開示または漏洩してはならない。契約番号 CNT-2025-0002 。',
  },
  {
    id: '11111111-0009-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '業務委託契約書\n契約番号： CNT-2025-0003\n甲：株式会社フューチャーテック\n乙：佐藤コンサルティング合同会社\n契約金額：年額240万円（税別）\n契約期間：2025年6月1日〜2026年5月31日',
  },
  {
    id: '11111111-0010-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第4条（知的財産権）\n乙が本業務の遂行に伴い作成した成果物の著作権は甲に帰属する。乙は甲に対し著作者人格権を行使しない。契約番号 CNT-2025-0003 。',
  },
  {
    id: '11111111-0011-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '業務委託契約書\n契約番号： CNT-2025-0004\n甲：合同会社デジタルソリューション\n乙：田中デザイン事務所\n委託内容：Webサイトデザイン業務\n契約金額：一式150万円（税別）',
  },
  {
    id: '11111111-0012-0000-0000-000000000000',
    documentTypeCode: 'contract',
    text: '第5条（損害賠償）\n乙の責に帰すべき事由により甲に損害が生じた場合、乙は甲に対し当該損害を賠償する。ただし賠償額は委託料の3ヶ月分を上限とする。契約番号 CNT-2025-0004 。',
  },

  // ── Invoices ───────────────────────────────────────────────────────────────
  {
    id: '22222222-0001-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書\n請求書番号： INV-2025-04-001\n発行日：2025年4月30日\n支払期日：2025年5月31日\n請求先：株式会社サンプル 御中\n発行者：山田商事株式会社',
  },
  {
    id: '22222222-0002-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書番号 INV-2025-04-001\nご請求内容：システム開発業務委託費（2025年4月分）\n単価：500,000円 × 1ヶ月\n小計：500,000円\n消費税（10%）：50,000円\n合計金額：550,000円',
  },
  {
    id: '22222222-0003-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書\n請求書番号： INV-2025-04-002\n発行日：2025年4月30日\n支払期日：2025年5月20日\n請求先：有限会社テスト工業 御中\n発行者：鈴木技研株式会社',
  },
  {
    id: '22222222-0004-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書番号 INV-2025-04-002\nご請求内容：製品検査業務委託費（2025年4月分）\n単価：800,000円 × 1ヶ月\n小計：800,000円\n消費税（10%）：80,000円\n合計金額：880,000円',
  },
  {
    id: '22222222-0005-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書\n請求書番号： INV-2025-03-007\n発行日：2025年3月31日\n支払期日：2025年4月30日\n品目：コンサルティングサービス料\n金額：¥330,000（税込）',
  },
  {
    id: '22222222-0006-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書番号 INV-2025-03-007\n振込先口座：○○銀行 新宿支店 普通預金 1234567\n口座名義：サトウコンサルティング\n請求金額合計：330,000円（消費税込み）',
  },
  {
    id: '22222222-0007-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書\n請求書番号： INV-2025-05-015\n発行日：2025年5月31日\n品目：Webデザイン制作費\n数量：1式\n金額：1,650,000円（税込）\n請求先：合同会社デジタルソリューション 御中',
  },
  {
    id: '22222222-0008-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書番号 INV-2025-05-015\nお支払い方法：銀行振込\n振込期日：2025年6月30日\n備考：本請求書は契約番号 CNT-2025-0004 に基づく最終精算分です。',
  },
  {
    id: '22222222-0009-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書\n請求書番号： INV-2025-02-003\n発行日：2025年2月28日\n品目：サーバー保守管理費（2025年2月分）\n金額：110,000円（税込）\n契約番号： SVC-2024-0088',
  },
  {
    id: '22222222-0010-0000-0000-000000000000',
    documentTypeCode: 'invoice',
    text: '請求書番号 INV-2025-02-003\n小計：100,000円\n消費税10%：10,000円\n合計：110,000円\n支払期日：2025年3月25日\n振込先：△△銀行 渋谷支店 普通 9876543',
  },

  // ── Certificates ───────────────────────────────────────────────────────────
  {
    id: '33333333-0001-0000-0000-000000000000',
    documentTypeCode: 'certificate',
    text: '在職証明書\n証明書番号： CERT-2025-EMP-0042\n氏名：山田 太郎（ヤマダ タロウ）\n社員番号： EMP-10042\n所属：株式会社サンプル 技術部\n入社年月日：2018年4月1日\n上記の者が当社に在職していることを証明します。',
  },
  {
    id: '33333333-0002-0000-0000-000000000000',
    documentTypeCode: 'certificate',
    text: '在職証明書\n証明書番号： CERT-2025-EMP-0043\n氏名：鈴木 花子（スズキ ハナコ）\n社員番号： EMP-10043\n所属：有限会社テスト工業 営業部\n入社年月日：2020年10月1日\n本証明書の有効期限は発行日より3ヶ月とする。',
  },
  {
    id: '33333333-0003-0000-0000-000000000000',
    documentTypeCode: 'certificate',
    text: '資格証明書\n証明書番号： CERT-2025-LIC-0201\n氏名：佐藤 次郎（サトウ ジロウ）\n資格名：情報処理安全確保支援士\n登録番号：第 012345 号\n登録日：2024年10月15日\n上記の者が当該資格を保有していることを証明します。',
  },
  {
    id: '33333333-0004-0000-0000-000000000000',
    documentTypeCode: 'certificate',
    text: '卒業証明書\n証明書番号： CERT-2025-EDU-0089\n氏名：田中 美咲（タナカ ミサキ）\n学籍番号： STU-20210123\n学部：工学部 情報工学科\n卒業年月日：2025年3月31日\n上記の者が所定の課程を修了し卒業したことを証明します。',
  },
  {
    id: '33333333-0005-0000-0000-000000000000',
    documentTypeCode: 'certificate',
    text: '健康診断証明書\n証明書番号： CERT-2025-MED-0307\n氏名：伊藤 健一（イトウ ケンイチ）\n生年月日：1985年7月22日\n診断日：2025年4月10日\n判定：異常なし\n本証明書は健康診断の結果を証明するものです。',
  },

  // ── Generic prose ─────────────────────────────────────────────────────────
  {
    id: '44444444-0001-0000-0000-000000000000',
    documentTypeCode: 'policy',
    text: '個人情報保護方針\n当社は、個人情報の保護に関する法律（個人情報保護法）を遵守し、個人情報を適切に管理します。収集した個人情報は、明示した利用目的の範囲内でのみ使用します。',
  },
  {
    id: '44444444-0002-0000-0000-000000000000',
    documentTypeCode: 'policy',
    text: '利用規約\n本規約は、当社が提供するサービスの利用条件を定めるものです。ユーザーは本規約に同意した上でサービスを利用してください。サービスの不正利用は禁止します。',
  },
  {
    id: '44444444-0003-0000-0000-000000000000',
    documentTypeCode: 'policy',
    text: '返品・返金ポリシー\n商品到着後7日以内であれば返品を受け付けます。未使用・未開封の商品に限ります。返送料はお客様負担とします。返金は確認後5営業日以内に処理します。',
  },
  {
    id: '44444444-0004-0000-0000-000000000000',
    documentTypeCode: 'policy',
    text: '配送について\n ご注文受付後、通常3〜5営業日以内に発送いたします。送料は全国一律660円（税込）です。5,500円（税込）以上のご注文で送料無料となります。',
  },
  {
    id: '44444444-0005-0000-0000-000000000000',
    documentTypeCode: 'policy',
    text: '保証規定\n製品は購入日より1年間の製品保証が適用されます。正常な使用状態での故障に限り無償修理を承ります。消耗品・外観上の損傷・水濡れは保証対象外です。',
  },
];

const queries = [
  // Precise-text — pure ID queries (user looking up a specific document by number).
  // Vector path confuses semantically-similar templates; keyword path wins by exact ID match.
  { id: 'q-p-01', category: 'precise-text', text: 'CNT-2025-0001', expectedChunkIds: ['11111111-0001-0000-0000-000000000000', '11111111-0002-0000-0000-000000000000', '11111111-0003-0000-0000-000000000000', '11111111-0004-0000-0000-000000000000'] },
  { id: 'q-p-02', category: 'precise-text', text: '契約番号 CNT-2025-0001 委託料', expectedChunkIds: ['11111111-0003-0000-0000-000000000000'] },
  { id: 'q-p-03', category: 'precise-text', text: 'CNT-2025-0002', expectedChunkIds: ['11111111-0005-0000-0000-000000000000', '11111111-0006-0000-0000-000000000000', '11111111-0007-0000-0000-000000000000', '11111111-0008-0000-0000-000000000000'] },
  { id: 'q-p-04', category: 'precise-text', text: '契約番号 CNT-2025-0002 秘密保持', expectedChunkIds: ['11111111-0008-0000-0000-000000000000'] },
  { id: 'q-p-05', category: 'precise-text', text: 'CNT-2025-0003', expectedChunkIds: ['11111111-0009-0000-0000-000000000000', '11111111-0010-0000-0000-000000000000'] },
  { id: 'q-p-06', category: 'precise-text', text: 'CNT-2025-0004 損害賠償', expectedChunkIds: ['11111111-0012-0000-0000-000000000000'] },
  // Precise-text — invoice IDs
  { id: 'q-p-07', category: 'precise-text', text: 'INV-2025-04-001', expectedChunkIds: ['22222222-0001-0000-0000-000000000000', '22222222-0002-0000-0000-000000000000'] },
  { id: 'q-p-08', category: 'precise-text', text: 'INV-2025-04-002', expectedChunkIds: ['22222222-0003-0000-0000-000000000000', '22222222-0004-0000-0000-000000000000'] },
  { id: 'q-p-09', category: 'precise-text', text: 'INV-2025-03-007', expectedChunkIds: ['22222222-0005-0000-0000-000000000000', '22222222-0006-0000-0000-000000000000'] },
  { id: 'q-p-10', category: 'precise-text', text: 'INV-2025-05-015', expectedChunkIds: ['22222222-0007-0000-0000-000000000000', '22222222-0008-0000-0000-000000000000'] },
  { id: 'q-p-11', category: 'precise-text', text: 'INV-2025-02-003', expectedChunkIds: ['22222222-0009-0000-0000-000000000000', '22222222-0010-0000-0000-000000000000'] },
  // Precise-text — certificate IDs / employee numbers
  { id: 'q-p-12', category: 'precise-text', text: 'CERT-2025-EMP-0042', expectedChunkIds: ['33333333-0001-0000-0000-000000000000'] },
  { id: 'q-p-13', category: 'precise-text', text: 'EMP-10043', expectedChunkIds: ['33333333-0002-0000-0000-000000000000'] },
  { id: 'q-p-14', category: 'precise-text', text: 'STU-20210123', expectedChunkIds: ['33333333-0004-0000-0000-000000000000'] },
  { id: 'q-p-15', category: 'precise-text', text: 'CERT-2025-LIC-0201', expectedChunkIds: ['33333333-0003-0000-0000-000000000000'] },
  // Semantic
  { id: 'q-s-01', category: 'semantic', text: '甲乙双方の責任範囲について教えてください', expectedChunkIds: ['11111111-0001-0000-0000-000000000000', '11111111-0005-0000-0000-000000000000', '11111111-0009-0000-0000-000000000000'] },
  { id: 'q-s-02', category: 'semantic', text: '業務委託の支払条件はどうなっていますか', expectedChunkIds: ['11111111-0003-0000-0000-000000000000', '11111111-0007-0000-0000-000000000000'] },
  { id: 'q-s-03', category: 'semantic', text: '契約の自動更新条件を確認したい', expectedChunkIds: ['11111111-0004-0000-0000-000000000000'] },
  { id: 'q-s-04', category: 'semantic', text: '成果物の著作権はどちらに帰属しますか', expectedChunkIds: ['11111111-0010-0000-0000-000000000000'] },
  { id: 'q-s-05', category: 'semantic', text: '情報漏洩を禁止する条項', expectedChunkIds: ['11111111-0008-0000-0000-000000000000'] },
  { id: 'q-s-06', category: 'semantic', text: '請求書の消費税額を確認したい', expectedChunkIds: ['22222222-0002-0000-0000-000000000000', '22222222-0004-0000-0000-000000000000', '22222222-0010-0000-0000-000000000000'] },
  { id: 'q-s-07', category: 'semantic', text: '銀行振込の口座情報を教えてください', expectedChunkIds: ['22222222-0006-0000-0000-000000000000', '22222222-0010-0000-0000-000000000000'] },
  { id: 'q-s-08', category: 'semantic', text: 'お支払い期日はいつですか', expectedChunkIds: ['22222222-0001-0000-0000-000000000000', '22222222-0003-0000-0000-000000000000', '22222222-0007-0000-0000-000000000000'] },
  { id: 'q-s-09', category: 'semantic', text: '在職を証明する書類が必要です', expectedChunkIds: ['33333333-0001-0000-0000-000000000000', '33333333-0002-0000-0000-000000000000'] },
  { id: 'q-s-10', category: 'semantic', text: '学歴証明について', expectedChunkIds: ['33333333-0004-0000-0000-000000000000'] },
  { id: 'q-s-11', category: 'semantic', text: 'プライバシー保護の方針について教えてください', expectedChunkIds: ['44444444-0001-0000-0000-000000000000'] },
  { id: 'q-s-12', category: 'semantic', text: '商品を返品したい場合の手続き', expectedChunkIds: ['44444444-0003-0000-0000-000000000000'] },
  { id: 'q-s-13', category: 'semantic', text: '発送にかかる日数と送料', expectedChunkIds: ['44444444-0004-0000-0000-000000000000'] },
  { id: 'q-s-14', category: 'semantic', text: '製品保証の期間と対象範囲', expectedChunkIds: ['44444444-0005-0000-0000-000000000000'] },
  { id: 'q-s-15', category: 'semantic', text: 'サービス利用上の禁止事項', expectedChunkIds: ['44444444-0002-0000-0000-000000000000'] },
];

const TARGET_DIM = 1536;

function zeroPad(arr) {
  const out = new Float32Array(TARGET_DIM);
  for (let i = 0; i < arr.length && i < TARGET_DIM; i++) out[i] = arr[i];
  return out;
}

function toBase64(floats) {
  const buf = Buffer.allocUnsafe(floats.length * 4);
  for (let i = 0; i < floats.length; i++) buf.writeFloatLE(floats[i], i * 4);
  return buf.toString('base64');
}

console.log('Loading multilingual embedding model...');
const extractor = await pipeline(
  'feature-extraction',
  'Xenova/paraphrase-multilingual-MiniLM-L12-v2',
  { revision: 'main' }
);
console.log('Model loaded.');

async function embed(text) {
  const result = await extractor(text, { pooling: 'mean', normalize: true });
  return zeroPad(Array.from(result.data));
}

console.log(`Embedding ${chunks.length} chunks...`);
const encodedChunks = [];
for (const c of chunks) {
  encodedChunks.push({
    id: c.id,
    text: c.text,
    documentTypeCode: c.documentTypeCode,
    embeddingBase64: toBase64(await embed(c.text)),
  });
  process.stdout.write('.');
}
console.log('\nChunks done.');

console.log(`Embedding ${queries.length} queries...`);
const encodedQueries = [];
for (const q of queries) {
  encodedQueries.push({
    id: q.id,
    text: q.text,
    category: q.category,
    expectedChunkIds: q.expectedChunkIds,
    embeddingBase64: toBase64(await embed(q.text)),
  });
  process.stdout.write('.');
}
console.log('\nQueries done.');

const dataset = {
  version: '1.0',
  embeddingDimension: TARGET_DIM,
  chunks: encodedChunks,
  queries: encodedQueries,
};

const outPath = 'core/test/Dignite.Paperbase.Application.Tests/Benchmarks/rag-gold-dataset.json';
writeFileSync(outPath, JSON.stringify(dataset, null, 2), 'utf-8');
console.log(`\nDataset written to ${outPath}`);
console.log(`  chunks: ${encodedChunks.length}, queries: ${encodedQueries.length}`);
