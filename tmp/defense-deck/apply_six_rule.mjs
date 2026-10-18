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
const BUILD = path.join(HERE, 'six-rule');
const SOURCE = path.join(ROOT, 'output/defense/Final-Defense-IMRAD-Design-Final.pptx');
const FINAL = path.join(ROOT, 'output/defense/Final-Defense-IMRAD-6x6-Ready.pptx');
const SKILL = 'C:/Users/justine mendoza/.codex/plugins/cache/openai-primary-runtime/presentations/26.1011.11113/skills/presentations';
const PYTHON = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe';
const FONT = 'Calibri';
const C = { navy: '#142C45', teal: '#007F86', gold: '#C98D32', body: '#33424D', muted: '#607282', pale: '#F3F6F8', white: '#FFFFFF', lightTeal: '#6AD0D4' };
await fs.mkdir(BUILD, { recursive: true });
await fs.mkdir(path.join(BUILD, 'runtime-temp'), { recursive: true });
process.env.TEMP = path.join(BUILD, 'runtime-temp');
process.env.TMP = process.env.TEMP;
process.env.RUNTIME_NODE_MODULES = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
process.env.RUNTIME_NODE = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe';
const { finalizePresentation, applyPresentationChartFont } = await import(pathToFileURL(path.join(SKILL, 'container_tools/artifact_tool_utils.mjs')).href);
const p = await PresentationFile.importPptx(await FileBlob.load(SOURCE));
const before = p.toProto();
const records = (await p.inspect({ kind: 'slide,shape,textbox,table,chart,image', maxChars: 300000 })).ndjson.split('\n').filter(Boolean).map(JSON.parse);
await fs.writeFile(path.join(BUILD, 'before-inspect.ndjson'), records.map(x => JSON.stringify(x)).join('\n'));
const audits = [];
const tableOwners = [];
const chartOwners = [];
const darkSlides = new Set([0, 21, 22, 23]);

function add(i, content, x, y, w, h, size = 32, options = {}) {
  const slide = p.slides.items[i];
  const sh = slide.shapes.add({ geometry: 'textbox', name: content.slice(0, 60), position: { left: x, top: y, width: w, height: h }, fill: 'none', line: { fill: 'none', width: 0 } });
  sh.text = content;
  sh.text.style = { typeface: FONT, fontSize: size, color: darkSlides.has(i) ? '#E3EBEF' : C.body, bold: false, alignment: 'left', verticalAlignment: 'top', autoFit: 'none', wrap: 'square', insets: 0, ...options };
  return sh;
}

function body(i, lines, { top = 147, step = 54, left = 36, width = 888, size = 32, color } = {}) {
  if (lines.length > 6) throw new Error(`Slide ${i + 1}: too many narrative lines`);
  for (const [j, line] of lines.entries()) {
    const words = line.trim().split(/\s+/).length;
    if (words > 6) throw new Error(`Slide ${i + 1}: ${words} words in ${line}`);
    add(i, line, left, top + j * step, width, 44, size, color ? { color } : {});
  }
  audits.push({ slide: i + 1, type: 'narrative', lines, maxWords: Math.max(...lines.map(x => x.split(/\s+/).length)) });
}

function table(i, values, { top = 143, left = 36, width = 888, height = 300, widths, size = 32, header = true, total = false } = {}) {
  if (values.length > 6) throw new Error(`Slide ${i + 1}: table exceeds six rows`);
  for (const row of values) {
    const words = row.join(' ').trim().split(/\s+/).length;
    if (words > 6) throw new Error(`Slide ${i + 1}: table row has ${words} words: ${row.join(' ')}`);
  }
  const t = p.slides.items[i].tables.add({ rows: values.length, columns: values[0].length, left, top, width, height, columnWidths: widths, values });
  t.cells.block({ row: 0, column: 0, rowCount: values.length, columnCount: values[0].length }).assign({ margins: { left: 10, right: 10, top: 5, bottom: 5 } });
  for (let r = 0; r < values.length; r++) {
    t.rows[r].height = height / values.length;
    for (let c = 0; c < values[0].length; c++) {
      const cell = t.getCell(r, c);
      const isHeader = header && r === 0;
      const isTotal = total && r === values.length - 1;
      cell.fill = isHeader ? C.navy : isTotal ? '#E7F3F3' : r % 2 ? C.white : C.pale;
      cell.text.style = { typeface: FONT, fontSize: size, color: isHeader ? C.white : isTotal ? C.teal : C.body, bold: isHeader || isTotal, alignment: c === 0 ? 'left' : values[0].length > 2 ? 'center' : 'left', verticalAlignment: 'middle', autoFit: 'none', insets: 0 };
    }
  }
  tableOwners.push(i + 1);
  audits.push({ slide: i + 1, type: 'table', rows: values, rowCount: values.length, maxWords: Math.max(...values.map(row => row.join(' ').split(/\s+/).length)) });
  return t;
}

function header(i, title, section) {
  const dark = darkSlides.has(i);
  add(i, section, 36, 26, 888, 26, 17, { color: dark ? C.lightTeal : C.teal, bold: true });
  add(i, title, 36, 65, 888, 62, 40, { color: dark ? C.white : C.navy, bold: true });
  add(i, `${String(i + 1).padStart(2, '0')} / 24`, 858, 514, 65, 18, 12, { color: dark ? '#AEC0CB' : C.muted, alignment: 'right' });
}

// Preserve the complete supporting explanations before condensing the screen.
for (const [i, slide] of p.slides.items.entries()) {
  const details = records.filter(r => r.kind === 'textbox' && r.slideIndex === i && !/^\d{2} \/ 24$/.test(r.text) && r.position.top > 125).map(r => r.text);
  for (const e of before.slides[i].elements.filter(x => x.table)) {
    details.push(...e.table.rows.map(row => row.cells.map(cell => cell.text ?? '').join(', ')));
  }
  if (details.length) slide.speakerNotes.text += `\n\nSupporting Detail\n${details.join('\n')}\n`;
  p.delete(records.filter(r => r.slideIndex === i && ['shape', 'textbox', 'table'].includes(r.kind)).map(r => r.id));
  slide.background.fill = darkSlides.has(i) ? C.navy : C.white;
  slide.hidden = false;
}

add(0, 'TAGUIG CITY UNIVERSITY', 48, 28, 864, 26, 20, { color: C.lightTeal, bold: true });
add(0, 'COLLEGE OF INFORMATION AND COMPUTING TECHNOLOGY', 48, 59, 864, 24, 17, { color: '#BDCDD6' });
add(0, 'A Risk-Based Two-Factor\nStudent Verification and Event\nAttendance Management System', 48, 113, 864, 155, 44, { color: C.white, bold: true });
add(0, 'Using Near Field Communication (NFC),\nPersonal Identification Number (PIN) Authentication,\nand Quick Response (QR) Credential Validation', 48, 294, 864, 115, 27, { color: C.lightTeal });
add(0, 'Justine G. Mendoza\nMichael Daniel M. Milallos\nSeanne Stiffel T. Sintay', 48, 423, 800, 101, 24, { color: C.white });
add(0, '01 / 24', 858, 514, 65, 18, 12, { color: '#AEC0CB', alignment: 'right' });

header(1, 'Pre-oral Evidence', 'PANEL RECOMMENDATIONS');
table(1, [
  ['Recommendation', 'Status'],
  ['01-02 Checklist/visuals', 'Included'],
  ['03 Raw responses', 'Packaging pending'],
  ['04 Test evidence', 'Selected tests documented'],
  ['05 UI appendices', 'Updates pending'],
], { widths: [468, 420], height: 290, size: 31 });
body(1, ['Complete evidence package remains pending.'], { top: 462, size: 28, color: C.muted });

header(2, 'Pre-oral Comparison Requirements', 'PANEL RECOMMENDATIONS');
table(2, [
  ['Recommendation', 'Status'],
  ['06-07 Manual baseline', 'Included'],
  ['08 Respondent counts', 'Included'],
  ['09-10 Same participants', 'Different groups used'],
  ['16 Use case', 'Mode alignment pending'],
  ['17 Survey t-test', 'Welch test included'],
], { widths: [468, 420], height: 336, size: 31 });

header(3, 'Pre-oral System Requirements', 'PANEL RECOMMENDATIONS');
table(3, [
  ['Recommendation', 'Status'],
  ['11 Login timestamp', 'Display needs verification'],
  ['12 Student timestamps', 'Implemented'],
  ['13 Result retention', '10 seconds'],
  ['14 Admin purge', 'Local cleanup only'],
  ['15 Data destruction', 'Cloud retention unresolved'],
], { widths: [468, 420], height: 336, size: 31 });

header(4, 'Problem and Objectives', 'I. INTRODUCTION');
body(4, [
  'Paper attendance separates checks from records.',
  'Integrate credential checks with attendance records.',
  'Configure Fast, Standard, and High-Security modes.',
  'Handle entry, exit, and event eligibility.',
  'Describe performance and perceived quality.',
  'Compare manual and system survey ratings.',
]);

header(5, 'Configured Verification Modes', 'I. INTRODUCTION');
table(5, [
  ['Mode', 'Credentials'],
  ['Fast', 'NFC'],
  ['Standard', 'NFC + PIN'],
  ['High-Security', 'NFC + PIN + QR'],
], { widths: [320, 568], height: 224, size: 32 });
body(5, ['Operators select the verification mode.', 'QR validates NFC-linked credential consistency.'], { top: 401, step: 48, size: 30 });

header(6, 'Research Design and Respondents', 'II. METHODOLOGY');
body(6, ['Developmental, descriptive-evaluative research', 'ISO/IEC 25010-based survey questionnaires'], { top: 140, step: 48, size: 31 });
table(6, [
  ['System students', '70'],
  ['Manual students', '70, separate group'],
  ['Security personnel', '5'],
  ['Faculty/organizer/admin', '5'],
], { top: 249, widths: [518, 370], height: 224, size: 31, header: false });

header(7, 'Controlled Observation', 'II. METHODOLOGY');
table(7, [
  ['Batch', 'Students', 'Rounds per mode'],
  ['Aug 12', '40', '20 + 20'],
  ['Aug 13', '30', '20 + 10'],
], { widths: [255, 205, 428], height: 174, size: 31 });
body(7, ['70 participants completed all three modes.', '20 cards reused after reset exits.', 'Fixed order: Fast, Standard, High-Security.'], { top: 351, step: 49, size: 30 });

header(8, 'System Architecture', 'II. METHODOLOGY');
table(8, [
  ['Component', 'Role'],
  ['ESP32 + PN532', 'NFC UID reading'],
  ['WinUI 3', 'Rules and timestamps'],
  ['MySQL', 'Local record storage'],
  ['Firestore', 'Cloud sync and backup'],
], { widths: [380, 508], height: 270, size: 30 });
body(8, ['Later additions require separate evaluation.'], { top: 451, size: 30 });

header(9, 'Prototype Interfaces', 'II. METHODOLOGY');
const uiLabels = ['Authentication', 'Admin Dashboard', 'Security Dashboard', 'Event Organizer', 'Active Monitoring', 'Student Kiosk'];
const uiImages = [...p.slides.items[9].images.items].sort((a, b) => a.frame.top - b.frame.top || a.frame.left - b.frame.left);
if (uiImages.length !== 6) throw new Error('Six interface screenshots required');
uiLabels.forEach((label, j) => add(9, label, 36 + (j % 3) * 298, j < 3 ? 129 : 296, 280, 35, 27, { color: C.navy, bold: true }));
body(9, ['Prototype screenshots. Current build in demonstration.'], { top: 475, size: 25, color: C.muted });

header(10, 'Statistical Treatment', 'II. METHODOLOGY');
body(10, [
  'Means and respondent-level sample SDs',
  'Welch independent-samples t-test',
  'Two-tailed threshold: alpha = 0.05',
  'H0: Equal overall mean survey ratings.',
  'H1: Different overall mean survey ratings.',
  't = mean difference / SE',
]);

header(11, 'Records and Selected Test Cases', 'III. RESULTS');
table(11, [
  ['Mode', 'Entries', 'Resets', 'Denials', 'Total'],
  ['Fast', '70', '70', '2', '142'],
  ['Standard', '70', '70', '12', '152'],
  ['High-Security', '70', '70', '4', '144'],
  ['Total', '210', '210', '18', '438'],
], { widths: [256, 158, 158, 158, 158], height: 290, size: 29, total: true });
body(11, ['16 listed PASS cases, limited scope.'], { top: 462, size: 28, color: C.muted });

header(12, 'Internal Latency (ms)', 'III. RESULTS');
table(12, [
  ['Mode', 'Mean', 'SD', 'DB'],
  ['Fast', '11.49', '2.20', '2.88'],
  ['Standard', '57.37', '17.46', '47.95'],
  ['High-Security', '54.06', '6.34', '44.90'],
], { left: 36, width: 455, top: 151, widths: [182, 91, 91, 91], height: 232, size: 27 });
body(12, ['70 successful entries per mode.', 'DB timing overlaps internal latency.'], { top: 424, step: 47, size: 28 });

header(13, 'PIN/QR Interaction (s)', 'III. RESULTS');
table(13, [
  ['Metric', 'Standard', 'High'],
  ['Mean', '2.505', '5.799'],
  ['Median', '2.035', '4.348'],
  ['SD', '1.152', '3.878'],
  ['Maximum', '6.875', '22.604'],
], { left: 545, width: 379, top: 155, widths: [135, 122, 122], height: 260, size: 26 });
body(13, ['Fast stage N/A. Each mode: n=70.'], { top: 467, size: 28, color: C.muted });

header(14, 'Manual Attendance Baseline', 'III. RESULTS');
add(14, 'Mean: 21.803 s', 36, 146, 435, 64, 43, { color: C.gold, bold: true });
body(14, ['SD: 4.436 s', 'Median: 21.135 s', 'Range: 13.21-37.63 s', '70 active-writing observations'], { left: 36, width: 435, top: 234, step: 53, size: 31 });
body(14, ['Task boundaries differ from system measures.'], { top: 472, size: 28, color: C.muted });

header(15, 'Recorded Entry Spacing and Rate', 'III. RESULTS');
table(15, [
  ['Mode', 'Gaps', 'Mean(s)', 'Gaps/min'],
  ['Fast', '66', '2.730', '21.98'],
  ['Standard', '66', '8.048', '7.46'],
  ['High-Security', '66', '12.129', '4.95'],
], { widths: [288, 140, 230, 230], height: 232, size: 30 });
body(15, ['Four rounds: 19+19+19+9 = 66 gaps.', 'Complete-round throughput remains unmeasured.'], { top: 416, step: 53, size: 30 });

header(16, 'Controlled Security Results', 'III. RESULTS');
table(16, [
  ['Measure', 'Rate', 'Recorded count'],
  ['Correct credential decisions', '100%', '222/222'],
  ['Denied/all records', '4.11%', '18/438'],
  ['Lockouts/PIN submissions', '1.32%', '2/152'],
], { widths: [450, 210, 228], height: 252, size: 29 });
body(16, ['Researcher-labelled outcomes under controlled conditions.', 'Demonstrations contributed to denials and lockouts.'], { top: 426, step: 47, size: 28 });

header(17, 'Student Survey Comparison', 'III. RESULTS');
body(17, ['Performance difference: 1.811 rating points.', 'Different students. Categories are descriptive comparisons.'], { top: 433, step: 46, size: 27 });

header(18, 'System Survey Evaluation by Group', 'III. RESULTS');
body(18, ['Highly Satisfactory', 'All three groups', 'Staff minimum: 3.45', 'Performance Efficiency', 'Admin: eight categories', 'Other groups: five'], { left: 617, width: 307, top: 148, step: 52, size: 27 });

header(19, 'Welch Survey Comparison', 'III. RESULTS');
table(19, [
  ['Group', 'n', 'Mean', 'Sample SD'],
  ['Manual process', '70', '2.284', '0.753'],
  ['Proposed system', '70', '3.742', '0.290'],
], { widths: [360, 110, 200, 218], height: 180, size: 31 });
body(19, ['t(89.076)=15.124, p<.001'], { top: 355, size: 36, color: C.teal });
body(19, ['Difference=1.458, 95% CI=[1.267, 1.650]'], { top: 413, size: 31 });
body(19, ['Reject H0: Overall survey ratings differ.'], { top: 469, size: 29, color: C.muted });

header(20, 'Study Limitations', 'IV. DISCUSSION');
body(20, [
  'Physical tailgating prevention remains unverified.',
  'Complete-task processing speed remains unmeasured.',
  'Shared cards limit individual-identity conclusions.',
  'Fixed mode order permits practice effects.',
  'Different survey groups limit causal interpretation.',
  'Small, non-random samples limit generalization.',
]);

header(21, 'Conclusion', 'IV. DISCUSSION');
body(21, [
  'Prototype integrates credential validation and attendance.',
  'Configured modes support different verification requirements.',
  'Controlled decisions matched researcher-labelled expectations.',
  'Respondents reported favorable perceived system quality.',
  'Overall student survey ratings differed significantly.',
  'Broader effectiveness requires further evaluation.',
]);

header(22, 'Further Evaluation', 'IV. DISCUSSION');
body(22, [
  'Measure equivalent complete tasks and throughput.',
  'Expand samples across campuses and roles.',
  'Counterbalance the verification mode order.',
  'Test credential sharing and outage recovery.',
  'Evaluate later additions separately.',
  'Complete evidence, retention, and destruction documentation.',
]);

add(23, 'SYSTEM DEMONSTRATION', 48, 126, 864, 34, 24, { color: C.lightTeal, bold: true });
add(23, 'Live Demonstration and Questions', 48, 202, 864, 76, 48, { color: C.white, bold: true });
body(23, ['Configured verification modes', 'Entry/exit timestamps and alerts', 'Authorized local cleanup'], { left: 48, width: 864, top: 322, step: 60, size: 34 });
add(23, '24 / 24', 858, 514, 65, 18, 12, { color: '#AEC0CB', alignment: 'right' });

const chartFrames = {
  12: { left: 486, top: 143, width: 438, height: 266 },
  13: { left: 36, top: 142, width: 485, height: 294 },
  14: { left: 467, top: 142, width: 457, height: 304 },
  17: { left: 36, top: 141, width: 888, height: 283 },
  18: { left: 36, top: 144, width: 557, height: 322 },
};
for (const [i, slide] of p.slides.items.entries()) {
  for (const ch of slide.charts.items) {
    chartOwners.push(i + 1);
    ch.position = chartFrames[i];
    applyPresentationChartFont(ch, { fontFamily: FONT });
    ch.xAxis = { textStyle: { typeface: FONT, fontSize: i === 12 || i === 14 ? 22 : i === 17 ? 23 : 24, fill: C.body } };
    ch.yAxis = { textStyle: { typeface: FONT, fontSize: 22, fill: C.muted } };
    ch.legend = { textStyle: { typeface: FONT, fontSize: 24, fill: C.body } };
    ch.dataLabels = { textStyle: { typeface: FONT, fontSize: i === 17 ? 26 : 30, bold: true, fill: i === 13 ? C.white : C.navy } };
    if (i === 14) ch.xAxis = { title: { text: 'Seconds', textStyle: { typeface: FONT, fontSize: 24, fill: C.body } } };
    if (i === 13) for (const series of ch.series.items) series.dataLabels = { showValue: true, position: 'center', numberFormatCode: '0.000', textStyle: { typeface: FONT, fontSize: 30, bold: true, fill: C.white } };
  }
}

await fs.writeFile(path.join(BUILD, 'six-rule-audit.json'), JSON.stringify(audits, null, 2));
await fs.writeFile(path.join(BUILD, 'authored-proto.json'), JSON.stringify(p.toProto()));
const draft = path.join(BUILD, 'candidate.pptx');
await (await PresentationFile.exportPptx(p)).save(draft);
await promisify(execFile)(PYTHON, [path.join(HERE, 'refine_table_borders.py'), draft, SOURCE, 'horizontal'], { cwd: ROOT });
await fs.writeFile(path.join(BUILD, 'source-sha256.txt'), createHash('sha256').update(await fs.readFile(SOURCE)).digest('hex'));
const result = await finalizePresentation({
  workspaceDir: ROOT, candidatePath: draft, finalPath: FINAL, pythonExecutable: PYTHON,
  integrityValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_package_integrity.py'),
  layoutValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_layout_geometry.py'),
  layoutArgs: ['--expected-slide-size-emu', '9144000,5143500', '--validate-heading-fit', ...tableOwners.flatMap(n => ['--require-native-table-slide', String(n)])],
  explicitTotalSlideCount: 24, requiredNativeTableOwnerSlides: tableOwners, requiredNativeChartOwnerSlides: chartOwners,
  requiredEmbeddedWorkbookChartOwnerSlides: chartOwners, materializeLiteralChartWorkbooks: false,
  fontPolicy: { basis: 'design', families: [FONT] }, verifyArtifactToolImport: true,
  receiptPath: path.join(BUILD, `${path.basename(FINAL)}.validation.json`),
});
console.log('Final deck:', result.finalPath);
const preview = path.join(BUILD, 'artifact-render');
await fs.mkdir(preview, { recursive: true });
for (const [i, slide] of p.slides.items.entries()) {
  const png = await p.export({ slide, format: 'png', scale: 1.5 });
  await fs.writeFile(path.join(preview, `slide-${String(i + 1).padStart(2, '0')}.png`), new Uint8Array(await png.arrayBuffer()));
  console.log('Rendered slide', i + 1);
}
