import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { promisify } from 'node:util';
import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { FileBlob, PresentationFile } from '@oai/artifact-tool';

process.on('uncaughtException', error => {
  console.error(error.name, error.message);
  console.error(error.stack?.split('\n').slice(1, 6).join('\n'));
  process.exitCode = 1;
});

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '../..');
const BUILD = path.join(HERE, 'design');
const SOURCE = path.join(ROOT, 'output/defense/Final-Defense-IMRAD-Revised.pptx');
const FINAL = path.join(ROOT, 'output/defense/Final-Defense-IMRAD-Design-Final.pptx');
const SKILL = 'C:/Users/justine mendoza/.codex/plugins/cache/openai-primary-runtime/presentations/26.1011.11113/skills/presentations';
const PYTHON = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe';
const FONT = 'Calibri';
const C = { navy: '#142C45', teal: '#007F86', gold: '#C98D32', body: '#33424D', muted: '#607282', pale: '#F3F6F8', line: '#DCE5E9', white: '#FFFFFF', lightTeal: '#6AD0D4' };
await fs.mkdir(BUILD, { recursive: true });
await fs.mkdir(path.join(BUILD, 'runtime-temp'), { recursive: true });
process.env.TEMP = path.join(BUILD, 'runtime-temp');
process.env.TMP = process.env.TEMP;
process.env.RUNTIME_NODE_MODULES = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
process.env.RUNTIME_NODE = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe';
const { finalizePresentation, applyPresentationChartFont } = await import(pathToFileURL(path.join(SKILL, 'container_tools/artifact_tool_utils.mjs')).href);
const p = await PresentationFile.importPptx(await FileBlob.load(SOURCE));
const before = p.toProto();
await fs.writeFile(path.join(BUILD, 'before-proto.json'), JSON.stringify(before));
const records = (await p.inspect({ kind: 'slide,textbox,shape,table,chart,image', maxChars: 250000 })).ndjson.split('\n').filter(Boolean).map(JSON.parse);
await fs.writeFile(path.join(BUILD, 'before-inspect.ndjson'), records.map(x => JSON.stringify(x)).join('\n'));
const textRecords = i => records.filter(r => r.kind === 'textbox' && r.slideIndex === i);
const find = (i, prefix) => {
  const r = textRecords(i).find(x => x.text.startsWith(prefix));
  if (!r) throw new Error(`Cannot locate slide ${i + 1} text: ${prefix}`);
  return p.resolve(r.id);
};
const set = (i, prefix, frame, style = {}, content) => {
  const sh = find(i, prefix);
  if (content !== undefined) sh.text = content;
  sh.position = frame;
  sh.text.style = { typeface: FONT, fontSize: 22, color: C.body, bold: false, alignment: 'left', verticalAlignment: 'top', autoFit: 'none', wrap: 'square', insets: 0, ...style };
  return sh;
};
const add = (slide, content, frame, style = {}) => {
  const sh = slide.shapes.add({ geometry: 'textbox', name: content.slice(0, 60), position: frame, fill: 'none', line: { fill: 'none', width: 0 } });
  sh.text = content;
  sh.text.style = { typeface: FONT, fontSize: 22, color: C.body, bold: false, alignment: 'left', verticalAlignment: 'top', autoFit: 'none', wrap: 'square', insets: 0, ...style };
  return sh;
};
const deleteShapes = i => {
  p.delete(records.filter(r => r.slideIndex === i && ['shape', 'textbox'].includes(r.kind)).map(r => r.id));
};

p.theme.defaultFont = FONT;
p.theme.colorScheme = { name: 'NFC Thesis Navy Teal', themeColors: {
  accent1: C.teal, accent2: C.gold, accent3: C.navy, accent4: '#668693', accent5: '#A9C7CE', accent6: '#506679',
  bg1: C.white, bg2: C.pale, tx1: C.body, tx2: C.muted, dk1: '#14212C', dk2: C.navy, lt1: C.white, lt2: C.line,
  hlink: C.teal, folHlink: C.navy,
} };

const darkSlides = new Set([0, 21, 22, 23]);
const tableOwners = [];
const chartOwners = [];
for (const [i, slide] of p.slides.items.entries()) {
  const dark = darkSlides.has(i);
  slide.background.fill = dark ? C.navy : C.white;
  slide.hidden = false;
  if ([0, 9, 23].includes(i)) continue;
  for (const r of textRecords(i)) {
    const sh = p.resolve(r.id);
    const style = r.style ?? {};
    const old = sh.position;
    const fontSize = style.fontSize ?? 22;
    const heading = style.bold === true;
    const color = dark ? '#E3EBEF' : C.body;
    sh.text.style = { typeface: FONT, fontSize, color, bold: heading, alignment: 'left', verticalAlignment: 'top', autoFit: 'none', wrap: 'square', insets: 0 };
    if (old.top < 48) {
      sh.position = { left: 36, top: 28, width: 885, height: 22 };
      sh.text.style = { fontSize: 13, color: dark ? C.lightTeal : C.teal, bold: true };
    } else if (old.top < 100) {
      sh.position = { left: 36, top: 65, width: 885, height: 54 };
      sh.text.style = { fontSize: 34, color: dark ? C.white : C.navy, bold: true };
    } else if (/^\d{2} \/ 24$/.test(r.text)) {
      sh.position = { left: 858, top: 512, width: 65, height: 18 };
      sh.text.style = { fontSize: 11, alignment: 'right', color: dark ? '#AEC0CB' : C.muted, bold: false };
    } else if (old.top >= 462) {
      sh.position = { left: 36, top: 479, width: 885, height: 33 };
      sh.text.style = { fontSize: 16, color: dark ? '#AEC0CB' : C.muted, bold: false };
    } else if (heading && fontSize < 30) {
      sh.text.style = { color: dark ? C.lightTeal : C.teal };
    }
    if (old.left === 32 || old.left === 38) sh.position = { ...sh.position, left: 36 };
  }
  for (const [tableIndex, t] of slide.tables.items.entries()) {
    const dimensions = records.filter(r => r.kind === 'table' && r.slideIndex === i)[tableIndex];
    const { rows, cols } = dimensions;
    tableOwners.push(i + 1);
    const all = t.cells.block({ row: 0, column: 0, rowCount: rows, columnCount: cols });
    all.assign({ margins: { left: 10, right: 10, top: 4, bottom: 4 } });
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const cell = t.getCell(r, c);
        cell.fill = r === 0 ? C.navy : r % 2 ? C.white : C.pale;
        cell.text.style = { typeface: FONT, fontSize: r === 0 ? (i === 12 || i === 13 ? 19 : 20) : (i === 13 ? 19 : i < 4 ? 20 : 21), color: r === 0 ? C.white : C.body, bold: r === 0, verticalAlignment: 'middle', autoFit: 'none', insets: 0 };
      }
    }
    if (i === 11) {
      for (let c = 0; c < cols; c++) {
        t.getCell(rows - 1, c).fill = '#E7F3F3';
        t.getCell(rows - 1, c).text.style = { color: C.teal, bold: true };
      }
    }
  }
  for (const ch of slide.charts.items) {
    chartOwners.push(i + 1);
    applyPresentationChartFont(ch, { fontFamily: FONT });
    ch.chartFill = C.white;
    ch.plotAreaFill = C.white;
    ch.xAxis = { textStyle: { typeface: FONT, fontSize: 18, fill: C.body }, line: { fill: C.line, width: 0.8 } };
    ch.yAxis = { textStyle: { typeface: FONT, fontSize: 16, fill: C.muted }, majorGridlines: { fill: C.line, width: 0.7 } };
    ch.legend = { position: 'bottom', overlay: false, textStyle: { typeface: FONT, fontSize: 18, fill: C.body } };
    ch.dataLabels = { textStyle: { typeface: FONT, fontSize: i === 17 ? 18 : 21, bold: true, fill: i === 13 ? C.white : C.navy } };
    for (const [j, series] of ch.series.items.entries()) series.fill = (i === 14 || (i === 17 && j === 0) || (i === 13 && j === 1)) ? C.gold : C.teal;
  }
}

// A flat cover keeps the formal title readable and matches the closing slides.
const cover = p.slides.items[0];
deleteShapes(0);
p.delete(records.filter(r => r.kind === 'image' && r.slideIndex === 0).map(r => r.id));
add(cover, 'TAGUIG CITY UNIVERSITY', { left: 48, top: 38, width: 864, height: 28 }, { fontSize: 17, bold: true, color: C.lightTeal });
add(cover, 'COLLEGE OF INFORMATION AND COMPUTING TECHNOLOGY', { left: 48, top: 70, width: 864, height: 26 }, { fontSize: 13, color: '#BDCDD6' });
add(cover, 'A Risk-Based Two-Factor\nStudent Verification and\nEvent Attendance Management System', { left: 48, top: 135, width: 864, height: 162 }, { fontSize: 42, bold: true, color: C.white });
add(cover, 'Using Near Field Communication (NFC),\nPersonal Identification Number (PIN) Authentication,\nand Quick Response (QR) Credential Validation', { left: 48, top: 326, width: 850, height: 80 }, { fontSize: 20, color: C.lightTeal });
add(cover, 'Justine G. Mendoza, Michael Daniel M. Milallos, Seanne Stiffel T. Sintay', { left: 48, top: 442, width: 864, height: 30 }, { fontSize: 18, color: C.white });
add(cover, 'BS Computer Science   Adviser: Prof. Ferdinand Rubio', { left: 48, top: 478, width: 830, height: 26 }, { fontSize: 15, color: '#BDCDD6' });
add(cover, '01 / 24', { left: 858, top: 512, width: 65, height: 18 }, { fontSize: 11, alignment: 'right', color: '#AEC0CB' });

// Keep the six real interface screenshots and remove their decorative frames.
const ui = p.slides.items[9];
const screenshots = [...ui.images.items].filter(im => im.frame.width > 150).sort((a, b) => a.frame.top - b.frame.top || a.frame.left - b.frame.left);
if (screenshots.length !== 6) throw new Error(`Expected six UI screenshots, found ${screenshots.length}`);
const securityBytes = await fs.readFile(path.join(BUILD, 'security-thesis-11.png'));
screenshots[2].replace({ blob: securityBytes.buffer.slice(securityBytes.byteOffset, securityBytes.byteOffset + securityBytes.byteLength), contentType: 'image/png', alt: 'Security Personnel dashboard, thesis Appendix D.4' });
deleteShapes(9);
p.delete(records.filter(r => r.kind === 'image' && r.slideIndex === 9 && r.position.width <= 150).map(r => r.id));
add(ui, 'II. METHODOLOGY  SYSTEM IMPLEMENTATION', { left: 36, top: 28, width: 885, height: 22 }, { fontSize: 13, color: C.teal, bold: true });
add(ui, 'Prototype Interfaces', { left: 36, top: 65, width: 885, height: 54 }, { fontSize: 34, color: C.navy, bold: true });
const uiLabels = ['Authentication', 'Admin Dashboard', 'Security Dashboard', 'Event Organizer', 'Active Monitoring', 'Student Kiosk'];
screenshots.forEach((im, j) => {
  const left = 36 + (j % 3) * 298;
  const top = j < 3 ? 163 : 330;
  const ratio = j === 2 ? 2048 / 1169 : im.frame.width / im.frame.height;
  const width = Math.min(280, 130 * ratio);
  const height = width / ratio;
  im.frame = { left: left + (280 - width) / 2, top, width, height };
  add(ui, uiLabels[j], { left, top: top - 28, width: 280, height: 25 }, { fontSize: 18, bold: true, color: C.navy });
});
add(ui, 'Selected prototype screenshots. The live demonstration shows the current build.', { left: 36, top: 479, width: 885, height: 33 }, { fontSize: 16, color: C.muted });
add(ui, '10 / 24', { left: 858, top: 512, width: 65, height: 18 }, { fontSize: 11, alignment: 'right', color: C.muted });
ui.speakerNotes.text += '\nSecurity Personnel dashboard screenshot: NFC Thesis Chapter 1-5 (1).pdf, Appendix D.4, printed page 211. The previous deck placed an event-organizer screenshot under the security label.\n';

// Balance evidence tables and the short explanatory text below them.
for (const i of [1, 2, 3]) {
  const t = p.slides.items[i].tables.items[0];
  t.position = { ...t.position, left: 36, top: 137, width: 888, height: i === 1 ? 312 : 318 };
  t.rows[0].height = 38;
  const rowCount = records.find(r => r.kind === 'table' && r.slideIndex === i).rows;
  for (let r = 1; r < rowCount; r++) t.rows[r].height = i === 1 ? 68 : 56;
}
set(5, 'QR checks consistency', { left: 36, top: 378, width: 880, height: 65 }, { fontSize: 23 });
set(7, 'Same participants across', { left: 36, top: 325, width: 430, height: 30 }, { fontSize: 23, bold: true, color: C.teal });
set(7, '70 participants completed', { left: 36, top: 365, width: 415, height: 100 }, { fontSize: 22 });
set(7, 'Shared test credentials', { left: 500, top: 325, width: 424, height: 30 }, { fontSize: 23, bold: true, color: C.teal });
set(7, '20 preconfigured NFC', { left: 500, top: 365, width: 415, height: 100 }, { fontSize: 22 });

set(10, 't = (mean System', { left: 36, top: 432, width: 880, height: 62 }, { fontSize: 22, color: C.teal }, 't = (mean System - mean Manual)\n     / sqrt(SD System^2 / n System + SD Manual^2 / n Manual)');
set(11, 'Recorded challenges', { left: 36, top: 370, width: 410, height: 30 }, { fontSize: 22, bold: true, color: C.teal });
set(11, 'Denials include deliberate', { left: 36, top: 408, width: 415, height: 62 }, { fontSize: 21 });
set(11, 'Documented functionality', { left: 502, top: 370, width: 420, height: 30 }, { fontSize: 22, bold: true, color: C.teal });
set(11, 'Appendix H lists', { left: 502, top: 408, width: 415, height: 62 }, { fontSize: 21 });
set(12, '70 successful entries', { left: 36, top: 424, width: 455, height: 30 }, { fontSize: 22, bold: true, color: C.teal });

set(14, '21.803 seconds', { left: 36, top: 142, width: 450, height: 66 }, { fontSize: 48, color: C.gold, bold: true });
set(14, 'Mean active writing', { left: 36, top: 215, width: 435, height: 34 }, { fontSize: 22 });
set(14, 'Sample SD 4.436', { left: 36, top: 283, width: 435, height: 105 }, { fontSize: 25 });

for (const [prefix, left, width] of [['100.00%', 36, 280], ['4.11%', 350, 280], ['1.32%', 662, 250]]) set(16, prefix, { left, top: 137, width, height: 60 }, { fontSize: 44, bold: true, color: C.teal });
set(16, '2 / 152 newly triggered', { left: 662, top: 207, width: 254, height: 58 }, { fontSize: 21 }, '2 new lockouts /\n152 PIN submissions');
set(16, '222 / 222 correct', { left: 36, top: 207, width: 280, height: 58 }, { fontSize: 21 });
set(16, '18 / 438 denied', { left: 350, top: 207, width: 280, height: 58 }, { fontSize: 21 });

set(18, 'Highly Satisfactory', { left: 574, top: 145, width: 340, height: 40 }, { fontSize: 27, color: C.teal, bold: true });
set(18, 'All three overall', { left: 574, top: 194, width: 340, height: 66 }, { fontSize: 22 });
set(18, 'Security personnel:', { left: 574, top: 277, width: 342, height: 78 }, { fontSize: 21 });
set(18, 'Admin: eight categories', { left: 574, top: 373, width: 342, height: 80 }, { fontSize: 21 });

set(19, 't(89.076)', { left: 36, top: 415, width: 485, height: 43 }, { fontSize: 30, color: C.teal, bold: true });
set(19, 'Difference 1.458', { left: 557, top: 408, width: 350, height: 64 }, { fontSize: 24 });

const closing = p.slides.items[23];
deleteShapes(23);
p.delete(records.filter(r => r.kind === 'image' && r.slideIndex === 23).map(r => r.id));
add(closing, 'SYSTEM DEMONSTRATION', { left: 48, top: 150, width: 864, height: 30 }, { fontSize: 18, bold: true, color: C.lightTeal });
add(closing, 'Live Demo and Questions', { left: 48, top: 215, width: 864, height: 70 }, { fontSize: 48, bold: true, color: C.white });
add(closing, 'Configured modes, entry/exit timestamps, alerts,\nand authorized local cleanup', { left: 48, top: 326, width: 835, height: 75 }, { fontSize: 24, color: '#BDCDD6' });
add(closing, '24 / 24', { left: 858, top: 512, width: 65, height: 18 }, { fontSize: 11, alignment: 'right', color: '#AEC0CB' });

await fs.writeFile(path.join(BUILD, 'authored-proto.json'), JSON.stringify(p.toProto()));
const draft = path.join(BUILD, 'candidate.pptx');
await (await PresentationFile.exportPptx(p)).save(draft);
// The native-table exporter omits requested border colors. Apply the same
// row-divider style to its OOXML cells without changing any table content.
await promisify(execFile)(PYTHON, [path.join(HERE, 'refine_table_borders.py'), draft, SOURCE], { cwd: ROOT });
const sourceHash = createHash('sha256').update(await fs.readFile(SOURCE)).digest('hex');
const result = await finalizePresentation({
  workspaceDir: ROOT, candidatePath: draft, finalPath: FINAL, pythonExecutable: PYTHON,
  integrityValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_package_integrity.py'),
  layoutValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_layout_geometry.py'),
  layoutArgs: ['--expected-slide-size-emu', '9144000,5143500', '--validate-heading-fit', ...[...new Set(tableOwners)].flatMap(n => ['--require-native-table-slide', String(n)])],
  explicitTotalSlideCount: 24,
  requiredNativeTableOwnerSlides: [...new Set(tableOwners)], requiredNativeChartOwnerSlides: [...new Set(chartOwners)],
  materializeLiteralChartWorkbooks: false,
  requiredEmbeddedWorkbookChartOwnerSlides: [...new Set(chartOwners)],
  fontPolicy: { basis: 'design', families: [FONT] }, verifyArtifactToolImport: true,
  receiptPath: path.join(BUILD, `${path.basename(FINAL)}.validation.json`),
});
await fs.writeFile(path.join(BUILD, 'source-sha256.txt'), sourceHash);
console.log('Final deck:', result.finalPath);
const preview = path.join(BUILD, 'artifact-render');
await fs.mkdir(preview, { recursive: true });
for (const [i, slide] of p.slides.items.entries()) {
  const png = await p.export({ slide, format: 'png', scale: 1.5 });
  await fs.writeFile(path.join(preview, `slide-${String(i + 1).padStart(2, '0')}.png`), new Uint8Array(await png.arrayBuffer()));
  console.log('Rendered slide', i + 1);
}
