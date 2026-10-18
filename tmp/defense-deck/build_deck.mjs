import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { FileBlob, PresentationFile } from '@oai/artifact-tool';

process.on('uncaughtException', error => {
  console.error(error.name, error.message);
  console.error(error.stack?.split('\n').slice(1, 6).join('\n'));
  process.exitCode = 1;
});

const BUILD = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(BUILD, '../..');
const SKILL = 'C:/Users/justine mendoza/.codex/plugins/cache/openai-primary-runtime/presentations/26.1009.11318/skills/presentations';
const PYTHON = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe';
const SOURCE = 'C:/Users/justine mendoza/Documents/Pre-oral_IMRAD.pptx';
const THESIS = 'C:/Users/justine mendoza/Documents/NFC Thesis Chapter 1-5 (1).pdf';
const FINAL = path.join(ROOT, 'output/defense/Final-Defense-IMRAD-Revised.pptx');
await fs.mkdir(path.join(BUILD, 'runtime-temp'), { recursive: true });
process.env.TEMP = path.join(BUILD, 'runtime-temp');
process.env.TMP = process.env.TEMP;
process.env.RUNTIME_NODE_MODULES = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
process.env.RUNTIME_NODE = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe';
const { finalizePresentation, applyPresentationChartFont } = await import(pathToFileURL(path.join(SKILL, 'container_tools/artifact_tool_utils.mjs')).href);
const p = await PresentationFile.importPptx(await FileBlob.load(SOURCE));
const sourceRecords = (await p.inspect({ kind: 'slide,textbox', maxChars: 200000 })).ndjson.split('\n').filter(Boolean).map(JSON.parse);
const record = (slideIndex, contains) => sourceRecords.find(x => x.kind === 'textbox' && x.slideIndex === slideIndex && x.text.includes(contains));
const cover = p.slides.items[2];
const interfaces = p.slides.items[9];
const demo = p.slides.items[17];
const layoutId = cover.layout?.id;
p.slides.keep([2, 9, 17]);

const C = { navy: '#1B2A4A', teal: '#0B7F8A', bright: '#0FA3B1', body: '#334155', muted: '#516385', line: '#D3DEEB', pale: '#F1F5F9', gold: '#D4872C', white: '#FFFFFF' };
const ordered = [];
const titles = [];
const tableOwners = [];
const chartOwners = [];

function text(slide, value, x, y, w, h, size = 22, options = {}) {
  const sh = slide.shapes.add({ geometry: 'textbox', name: options.name ?? value.slice(0, 45), position: { left: x, top: y, width: w, height: h }, fill: 'none', line: { fill: 'none', width: 0 } });
  sh.text = value;
  sh.text.style = { typeface: options.font ?? 'Calibri', fontSize: size, color: options.color ?? C.body, bold: options.bold ?? false, alignment: options.align ?? 'left', verticalAlignment: 'top', autoFit: 'none', wrap: 'square', insets: 0 };
  return sh;
}

function notes(slide, pages, content) {
  slide.speakerNotes.text = `${content}\n\nSource: ${THESIS}, printed pages ${pages}.\nPre-oral recommendations: supplied list and annotated manuscript photographs. Current implementation status follows the inspected code and pre-oral matrix.\n`;
}

function base(title, section, dark = false) {
  const slide = p.slides.add({ ...(layoutId ? { layoutId } : {}), background: { fill: dark ? '#132A52' : '#FFFFFF' } });
  ordered.push(slide.id.startsWith('sl/') ? slide.id : `sl/${slide.id}`);
  titles.push(title);
  text(slide, section, 32, 24, 865, 22, 14, { bold: true, color: dark ? '#5EC4CE' : C.teal });
  text(slide, title, 32, 57, 890, 65, 32, { font: 'Cambria', bold: true, color: dark ? C.white : C.navy });
  text(slide, `${String(ordered.length).padStart(2, '0')} / 24`, 862, 512, 65, 18, 11, { align: 'right', color: dark ? '#A5B7D5' : '#8FA0BE' });
  return slide;
}

function foot(slide, value, y = 462) {
  text(slide, value, 32, y, 895, 42, 18, { color: C.muted });
}

function table(slide, values, x, y, w, h, widths, size = 20) {
  const t = slide.tables.add({ rows: values.length, columns: values[0].length, left: x, top: y, width: w, height: h, columnWidths: widths, values });
  t.borders.assign({ style: 'solid', fill: C.line, width: 0.8 });
  t.cells.block({ row: 0, column: 0, rowCount: values.length, columnCount: values[0].length }).assign({ margins: { left: 9, right: 9, top: 3, bottom: 3 } });
  for (let r = 0; r < values.length; r++) {
    for (let c = 0; c < values[r].length; c++) {
      const cell = t.getCell(r, c);
      cell.fill = r === 0 ? '#E2E8F0' : (r % 2 ? '#FFFFFF' : '#F8FAFC');
      cell.text.style = { typeface: 'Calibri', fontSize: size, color: r === 0 ? C.teal : C.body, bold: r === 0, alignment: c > 0 && values[0].length > 3 ? 'center' : 'left', verticalAlignment: 'middle', insets: 0, autoFit: 'none' };
    }
  }
  tableOwners.push(ordered.length);
  return t;
}

function chart(slide, categories, series, frame, config = {}) {
  const ch = slide.charts.add('bar', {
    position: frame,
    categories,
    series,
    barOptions: { direction: 'column', grouping: 'clustered', gapWidth: 65, ...config.barOptions },
    hasLegend: series.length > 1,
    legend: { position: 'bottom', overlay: false, textStyle: { typeface: 'Calibri', fontSize: 18, fill: C.body } },
    xAxis: { textStyle: { typeface: 'Calibri', fontSize: 18, fill: C.body }, line: { fill: C.line, width: 1 } },
    yAxis: { min: 0, textStyle: { typeface: 'Calibri', fontSize: 16, fill: C.body }, majorGridlines: { fill: '#E2E8F0', width: 0.6 }, numberFormatCode: '0', ...config.yAxis },
    dataLabels: { showValue: true, position: 'outEnd', numberFormatCode: '0.00', textStyle: { typeface: 'Calibri', fontSize: 20, bold: true, fill: C.navy }, ...config.dataLabels },
    chartFill: '#FFFFFF', chartLine: { fill: 'none', width: 0 }, plotAreaFill: '#FFFFFF', plotAreaLine: { fill: 'none', width: 0 },
  });
  applyPresentationChartFont(ch, { fontFamily: 'Calibri' });
  chartOwners.push(ordered.length);
  return ch;
}

function paragraphs(slide, rows, y = 135, spacing = 88, width = 880, size = 23, dark = false) {
  rows.forEach(([heading, body], i) => {
    text(slide, heading, 38, y + i * spacing, width, 28, size, { bold: true, color: dark ? '#5EC4CE' : C.teal });
    text(slide, body, 38, y + i * spacing + 31, width, spacing - 35, size - 1, { color: dark ? '#E2E8F0' : C.body });
  });
}

// Preserve the source cover artwork, background, dimensions, and typography.
ordered.push(cover.id.startsWith('sl/') ? cover.id : `sl/${cover.id}`);
titles.push('Thesis Title');
p.resolve(record(2, 'TAGUIG CITY').id).text = 'TAGUIG CITY UNIVERSITY  COLLEGE OF INFORMATION AND COMPUTING TECHNOLOGY';
const sub = p.resolve(record(2, 'Using NFC').id);
sub.text = 'Using Near Field Communication (NFC), Personal Identification Number (PIN) Authentication, and Quick Response (QR) Credential Validation';
sub.position = { left: 64, top: 276, width: 835, height: 68 };
sub.text.style = { typeface: 'Inter', fontSize: 18, color: '#0FA3B1', autoFit: 'none', insets: 0 };
p.resolve(record(2, 'Justine').id).text = 'Justine G. Mendoza, Michael Daniel M. Milallos, Seanne Stiffel T. Sintay';
p.resolve(record(2, 'BS Computer').id).text = 'BS Computer Science   Adviser: Prof. Ferdinand Rubio';
p.resolve(record(2, '03 /').id).text = '01 / 24';
notes(cover, '1', 'Final defense presentation. The formal thesis title remains unchanged. Fast mode uses NFC alone, while Standard and High-Security add PIN. QR provides a credential-integrity check rather than an independent third authentication factor.');

let s = base('Pre-oral Recommendations on Evidence', 'PANEL RECOMMENDATIONS');
table(s, [
  ['Recommendation', 'Action and Present Status'],
  ['01-02  Checklist and Chapter 4 visuals', 'Complete checklist, corrected results, item tables, and comparison figures.'],
  ['03  Raw survey and comparison records', 'Student workbook and exports available. Full anonymized records still need a traceable evidence package.'],
  ['04  Evidence for tests and surveys', '16 listed test cases and appendix photographs. Evidence does not cover every current feature.'],
  ['05  Interfaces in appendices', 'Appendix D includes selected interfaces. Current timestamp and maintenance screenshots remain to be added.'],
], 32, 135, 895, 284, [325, 570], 21);
foot(s, 'Selected evidence appears in Appendices D and H-K. Pending evidence remains identified.');
notes(s, '205-212, 230-242', 'These are pre-oral recommendations from September 2, 2026, not the earlier title-defense comments. The full revision matrix lists each recommendation individually. Appendix I contains tally screenshots and selected sheets, not every respondent-level answer or exported transaction. Do not claim complete raw-data publication or comprehensive test evidence.');

s = base('Pre-oral Recommendations on Comparison', 'PANEL RECOMMENDATIONS');
table(s, [
  ['Recommendation', 'Action and Present Status'],
  ['06-07  Manual baseline', 'Manual questionnaire, 70 writing observations, and 70 manual survey responses included.'],
  ['08  Student participant counts', '70 manual students and 70 different system students. System survey also includes two groups of five.'],
  ['09-10  Same students and survey sequence', 'Not fulfilled as specified. Different groups require an independent-samples analysis.'],
  ['16  Use case diagram', 'Diagram included. Conditional PIN use still needs alignment with NFC-only Fast mode.'],
  ['17  T-test of manual and system means', 'Welch test compares overall survey scores. Equivalent complete-task times were unavailable.'],
], 32, 128, 895, 310, [325, 570], 20);
foot(s, 'Equal group sizes do not mean the same participants or eliminate group-selection bias.');
notes(s, '50-53, 64-67, 88-92, 123-128, 158-163, 225-226', 'Recommendations 9 and 10 remain methodological limitations. Both questionnaires exist, but the records do not demonstrate a paired manual-then-system sequence with the same students. Welch is appropriate for the supplied independent records and does not substitute for complying with a same-participant requirement. Processing-time inference needs a new comparison with equivalent task boundaries.');

s = base('Pre-oral Recommendations on the System', 'PANEL RECOMMENDATIONS');
table(s, [
  ['Recommendation', 'Current Implementation and Qualification'],
  ['11  Personnel login timestamp', 'Login audit timestamp recorded. A dedicated visible login-time field remains unverified.'],
  ['12  Student timestamps', 'Kiosk code displays scan time and applicable IN/OUT values.'],
  ['13  Retain student result', '10-second result display. Persistent last-log display still needs confirmation.'],
  ['14  Administrator purge', 'Authorized active cleanup removes eligible cloud-backed local logs.'],
  ['15  Data destruction', 'Partial: local cleanup retains cloud history. Full disposal remains unverified.'],
], 32, 128, 895, 302, [300, 595], 20);
foot(s, 'Photo 18: full title terms added. Implementation status still needs dated UI evidence.');
notes(s, '1, 205-212', 'Additional title instruction comes from the annotated pre-oral photograph. Code evidence: MainWindow.xaml.cs logs STAFF_LOGIN and ADMIN_LOGIN. SecurityDashboardWindow displays DisplayTime in audit feeds. KioskModeWindow.xaml.cs defines ResultDisplayDuration as 10 seconds and sets StudentScanTimeText. SecurityDashboardWindow.xaml.cs exposes authorized maintenance. DatabaseService.PurgeOldLogsAsync deletes local records only after synced_to_cloud confirmation. Cloud history remains. No destructive purge was executed for this presentation review.');

s = base('Problem and Research Objectives', 'I. INTRODUCTION');
paragraphs(s, [
  ['Problem', 'Campus verification and event attendance require credential checks, eligibility decisions, and traceable records. Paper sign-in alone does not perform these checks.'],
  ['Development objective', 'Develop a prototype that combines configurable credential validation with entry/exit handling, event attendance, and role-based monitoring.'],
  ['Evaluation objective', 'Describe system metrics and perceived quality. Compare manual and system survey ratings while preserving the limits of the timing data.'],
], 135, 106, 880, 24);
notes(s, '3-8', 'Avoid repeating unsupported claims of reduced queues, prevented fraud, or eliminated workload. SOP 6 asks about processing time, throughput, and corresponding survey-category means. Only the survey overall-score comparison supports the inferential t-test with the supplied data. The timing observations support descriptive component results.');

s = base('Scope and Configured Verification Modes', 'I. INTRODUCTION');
table(s, [
  ['Mode', 'Required Credentials', 'Selection'],
  ['Fast', 'NFC only', 'Operator or event configuration'],
  ['Standard', 'NFC and PIN', 'Operator or event configuration'],
  ['High-Security', 'NFC, PIN, and QR validation', 'Operator or event configuration'],
], 32, 138, 895, 194, [205, 370, 320], 23);
text(s, 'QR checks consistency with the NFC-linked credential record. It does not independently prove physical identity.', 38, 356, 880, 56, 23);
foot(s, 'The evaluated prototype used configured modes rather than a per-student automatic risk score.');
notes(s, '101-105, 112-114, 184-187', 'Fast is single-factor NFC. Standard uses possession plus knowledge. High-Security adds QR credential validation, not an independent third factor. Configured eligibility checks and entry/exit rules remain distinct from proving exclusive credential ownership. The same 20 preconfigured cards were shared across 70 test participants.');

s = base('Research Design and Respondents', 'II. METHODOLOGY');
text(s, 'Quantitative developmental and descriptive-evaluative design', 38, 133, 880, 30, 24, { bold: true, color: C.teal });
text(s, 'Iterative Waterfall development with controlled observation and ISO/IEC 25010-based questionnaires. Purposive and convenience sampling.', 38, 177, 875, 62, 23);
table(s, [
  ['Survey Group', 'n', 'Evaluation'],
  ['Students, proposed system', '70', 'Five common quality categories'],
  ['Students, manual process', '70', 'Five quality categories'],
  ['Security personnel', '5', 'Five quality categories'],
  ['Faculty, organizer, administrators', '5', 'Eight quality categories'],
], 32, 263, 895, 183, [475, 70, 350], 20);
foot(s, '150 survey respondents in total. The two student survey groups contain different people.');
notes(s, '48-53, 129-131, 135-158', '80 respondents evaluated the proposed system: 70 students, five security personnel, and five faculty/event-organizer/administrator respondents. A separate manual group contributed 70 responses. Do not present 150 as the proposed-system-only sample. Questionnaire totals are 24 items for the five-category student/security instruments and 39 items for the administrative instrument. These non-probability samples limit generalization.');

s = base('Controlled Observation Procedure', 'II. METHODOLOGY');
table(s, [
  ['Session', 'Participants', 'Entry Rounds per Mode'],
  ['August 12, 2026', '40', '20 entries, reset exits, then 20 entries'],
  ['August 13, 2026', '30', '20 entries, reset exits, then 10 entries'],
], 32, 136, 895, 150, [240, 145, 510], 23);
paragraphs(s, [
  ['Same participants across modes', '70 participants completed Fast, Standard, then High-Security in that fixed order. Each mode contains 70 successful entries.'],
  ['Shared test credentials', '20 preconfigured NFC cards with PIN and QR were reused. Team exit transactions reset the profiles between rounds.'],
], 308, 84, 880, 23);
notes(s, '113-114', 'There are 210 successful entry trials, not 210 distinct people. Across both dates, each mode contains rounds of 20, 20, 20, and 10. Team reset exits are preserved in the record audit but excluded from participant entry timing and credential-decision accuracy. Explanations occur before each subsequent mode. Fixed order allows practice or order effects. Shared test credentials limit claims about each participant\'s personal identity.');

s = base('System Architecture and Evaluation Scope', 'II. METHODOLOGY');
table(s, [
  ['Layer', 'Role'],
  ['ESP32 and PN532', 'Read NFC UID and forward it through USB serial.'],
  ['WinUI 3 application', 'Validate credentials, apply rules, and timestamp records.'],
  ['Local MySQL', 'Store student, event, attendance, and security records.'],
  ['Firebase Firestore', 'Support cloud synchronization and backup.'],
], 32, 133, 895, 225, [250, 645], 22);
text(s, 'Added after the August observation', 38, 381, 880, 29, 23, { bold: true, color: C.teal });
text(s, 'Signed QR credentials, durable offline queues, event-completion rules, and graduation clearance require separate evaluation evidence.', 38, 419, 875, 66, 23);
notes(s, '98-105, 112, 214', 'The ESP32 forwards captured input. The Windows application handles validation, timestamping, database operations, and cloud sync. Do not assert a universal sub-15-ms database latency. The researcher confirmed that signed QR, durable queues, event completion, and graduation clearance came after August 12-13. The final architecture may include them, but the August metrics and earlier surveys do not validate those additions. Selected historical disconnection/sync cases do not establish recovery under every outage or power failure.');

// Retain the source interface images and their placements rather than invent screenshots.
ordered.push(interfaces.id.startsWith('sl/') ? interfaces.id : `sl/${interfaces.id}`);
titles.push('Prototype Interfaces');
p.resolve(record(9, 'III. Results').id).text = 'II. METHODOLOGY  SYSTEM IMPLEMENTATION';
p.resolve(record(9, 'Implemented System').id).text = 'Prototype Interfaces';
p.resolve(record(9, 'Live working').id).text = 'Selected prototype screenshots. The live demonstration shows the current build.';
p.resolve(record(9, '10 /').id).text = '10 / 24';
notes(interfaces, '205-212', 'These are screenshots retained from the supplied deck, not fabricated current captures. Roles include administrator, security personnel, organizer, and student-facing verification. Check current permissions in the live build. Gate flow checks student status and campus entry/exit state. Event attendance checks event eligibility and records event check-in/checkout. Current event completion or clearance features require separately documented tests.');

s = base('Statistical Treatment and Hypotheses', 'II. METHODOLOGY');
paragraphs(s, [
  ['Descriptive analysis', 'Frequency, mean, and sample SD use applicable observations. Respondent category scores produce the category and overall composite SDs.'],
  ['Welch independent-samples t-test', 'Two-tailed test of overall student survey scores at alpha = 0.05. Different students form the manual and proposed-system groups.'],
], 133, 96, 880, 23);
text(s, 'H0: No significant difference in mean overall survey ratings.', 38, 336, 880, 34, 23, { bold: true });
text(s, 'H1: A significant difference exists in mean overall survey ratings.', 38, 380, 880, 34, 23);
text(s, 't = (mean System - mean Manual) / sqrt(SD System^2 / n System + SD Manual^2 / n Manual)', 38, 435, 880, 48, 21, { color: C.teal });
notes(s, '64-67, 158-163', 'Formally H0 is equality of population mean overall ratings, mu S = mu M. H1 is mu S not equal to mu M. Welch does not assume equal variances and uses Welch-Satterthwaite degrees of freedom. The overall score gives equal weight to five category means, not equal weight to all 24 individual items. Use sample SD with n - 1. Do not average item SDs to obtain a composite SD. Category means are descriptive. No separate category hypothesis tests or processing-time t-test were performed.');

s = base('Observation Records and Functionality Cases', 'III. RESULTS');
table(s, [
  ['Mode', 'Successful Entries', 'Reset Exits', 'Denied Records', 'Total'],
  ['Fast', '70', '70', '2', '142'],
  ['Standard', '70', '70', '12', '152'],
  ['High-Security', '70', '70', '4', '144'],
  ['Total', '210', '210', '18', '438'],
], 32, 132, 895, 222, [235, 190, 145, 170, 155], 22);
paragraphs(s, [
  ['Recorded challenges', 'Denials include deliberate PIN/lockout, QR mismatch, and entry/exit state tests.'],
  ['Documented functionality', 'Appendix H lists 16 PASS cases. These do not validate every feature or later addition.'],
], 375, 63, 880, 22);
notes(s, '113, 120-123, 230-231', 'Total granted records are 420: 210 participant entries plus 210 team reset exits. The 18 denials belong to different conditions. Do not label them spontaneous participant failures. The appendix case list records selected functional outcomes rather than independently reproduced hardware testing during this preparation. Later signed-QR, durable-queue, completion, and clearance features need separate dated cases.');

s = base('Internal System Latency', 'III. RESULTS  SUCCESSFUL ENTRIES');
table(s, [
  ['Mode', 'Mean ms', 'Sample SD ms', 'DB Mean ms'],
  ['Fast', '11.49', '2.20', '2.88'],
  ['Standard', '57.37', '17.46', '47.95'],
  ['High-Security', '54.06', '6.34', '44.90'],
], 32, 147, 475, 227, [165, 90, 120, 100], 21);
chart(s, ['Fast', 'Standard', 'High-Security'], [{ name: 'Mean internal latency', values: [11.49, 57.37, 54.06], fill: C.bright }], { left: 532, top: 137, width: 393, height: 288 }, { yAxis: { max: 80, majorUnit: 20, title: 'Milliseconds' } });
text(s, '70 successful entries per mode', 38, 397, 455, 30, 23, { bold: true, color: C.teal });
foot(s, 'Database-query time overlaps internal latency. Adding it again would double-count time.');
notes(s, '114-117', 'Mean internal latency is a logged component, not complete NFC-presentation-to-finished-verification duration. Standard has higher mean latency and dispersion than High-Security, so these results do not show a simple monotonic increase with security steps. Medians are 11.30, 50.66, and 52.64 ms. Ranges are 8.26-26.36, 44.55-137.47, and 49.44-86.36 ms. Database time is reported separately but overlaps internal latency.');

s = base('Recorded PIN and QR Interaction', 'III. RESULTS  SUCCESSFUL ENTRIES');
chart(s, ['Standard', 'High-Security'], [
  { name: 'PIN', values: [2.505, 2.162], fill: C.teal },
  { name: 'QR', values: [0, 3.637], fill: C.gold, dataLabels: { showValue: true, showSeriesName: false, showCategoryName: false, showLegendKey: false, showPercent: false, position: 'center', numberFormatCode: '0.000', textStyle: { typeface: 'Calibri', fontSize: 20, bold: true, fill: '#FFFFFF' } }, dataLabelOverrides: [{ idx: 0, showValue: false, showSeriesName: false, showCategoryName: false, showLegendKey: false, showPercent: false }] },
], { left: 32, top: 129, width: 500, height: 289 }, { barOptions: { grouping: 'stacked' }, dataLabels: { position: 'center', numberFormatCode: '0.000', textStyle: { typeface: 'Calibri', fontSize: 20, bold: true, fill: '#FFFFFF' } }, yAxis: { max: 7, majorUnit: 1, title: 'Seconds' } });
table(s, [
  ['Total Stage Duration', 'Standard', 'High-Security'],
  ['Mean (s)', '2.505', '5.799'],
  ['Median (s)', '2.035', '4.348'],
  ['Sample SD (s)', '1.152', '3.878'],
  ['Maximum (s)', '6.875', '22.604'],
], 557, 153, 370, 220, [165, 96, 109], 19);
foot(s, 'n = 70 per mode. Fast bypasses PIN/QR. Its interaction stage is N/A, not a zero-second full task.');
notes(s, '115-117', 'High-Security PIN mean 2.162 plus QR mean 3.637 gives 5.799 seconds. Standard includes PIN only. These logged stages do not include every physical task component. All successful observations, including slow trials, remain included. The High-Security maximum is 22.604 seconds. Lower High-Security PIN mean does not prove improvement because the fixed sequence placed it after Standard and practice may affect performance. The exports alone do not identify why QR trials took longer.');

s = base('Manual Paper Attendance Baseline', 'III. RESULTS  MANUAL OBSERVATION');
const manual = JSON.parse(await fs.readFile(path.join(ROOT, 'output/chapter4/manual.json'), 'utf8'));
const edges = [13, 18, 23, 28, 33, 38];
const counts = edges.slice(0, -1).map((a, i) => manual.observations.filter(x => x.seconds >= a && x.seconds < edges[i + 1]).length);
if (counts.reduce((a, b) => a + b, 0) !== 70) throw new Error('Manual histogram must contain 70 observations');
text(s, '21.803 seconds', 38, 142, 445, 60, 40, { bold: true, color: C.teal });
text(s, 'Mean active writing duration, n = 70', 38, 204, 450, 31, 23);
text(s, 'Sample SD 4.436 s\nMedian 21.135 s\nRange 13.21-37.63 s', 38, 257, 440, 96, 24);
chart(s, ['13-<18', '18-<23', '23-<28', '28-<33', '33-<38'], [{ name: 'Writing observations', values: counts, fill: C.bright }], { left: 492, top: 131, width: 431, height: 270 }, { dataLabels: { numberFormatCode: '0' }, yAxis: { title: 'Observations' } });
text(s, 'Duration bins in seconds', 600, 410, 280, 25, 19, { align: 'center', color: C.muted });
foot(s, 'Stopwatch: first written character to completed name, course/section, student ID, and signature.');
notes(s, '60, 123-125, 232-233', 'Manual event-attendance simulation included writing the required information. Personnel did not perform an ID or enrollment check. Timings exclude approach, waiting, transitions, and later record transcription. Mean 21.803 seconds is active writing, not a complete verified-entry task. The service-time-equivalent rate is 60 / 21.803 = 2.75 writing completions per minute and is not independently observed queue throughput. Histogram bins include their lower edge and exclude their upper edge. Source raw manual observations: output/chapter4/manual.json and the supplied manual baseline PDF.');

s = base('Recorded Entry Spacing and Rate', 'III. RESULTS  SUPPLEMENTARY MEASURE');
table(s, [
  ['Mode', 'Intervals', 'Mean Spacing s', 'Intervals per Minute'],
  ['Fast', '66', '2.730', '21.98'],
  ['Standard', '66', '8.048', '7.46'],
  ['High-Security', '66', '12.129', '4.95'],
], 32, 133, 895, 197, [225, 135, 255, 280], 23);
text(s, 'Why 66 intervals?', 38, 353, 850, 30, 24, { bold: true, color: C.teal });
text(s, 'Rounds of 20, 20, 20, and 10 entries produce 19 + 19 + 19 + 9 gaps between consecutive entries.', 38, 392, 860, 58, 24);
foot(s, 'These rates do not establish full-round capacity or a matched throughput advantage over manual attendance.');
notes(s, '117-119, 124, 187', 'The intervals are timestamp differences between successive successful entries within each round. First-entry latency is not represented. Sum spans are 180.200, 531.167, and 800.508 seconds. Rate equals 60 times 66 divided by summed seconds. Between-round resets and explanations are excluded. Within-round pauses and denials remain in elapsed time, including 13 denied records within selected spans. The manual 2.75 per minute is a service-time-equivalent calculation with a different denominator. Do not calculate a speed multiplier, peak capacity, or percentage improvement from these unlike measures. Independent complete-round timings were unavailable.');

s = base('Credential Decisions and Security Conditions', 'III. RESULTS');
text(s, '100.00%', 38, 135, 280, 52, 37, { bold: true, color: C.teal });
text(s, '222 / 222 correct applicable\ncredential decisions', 38, 195, 280, 57, 21);
text(s, '4.11%', 350, 135, 280, 52, 37, { bold: true, color: C.teal });
text(s, '18 / 438 denied\nexported records', 350, 195, 280, 57, 21);
text(s, '1.32%', 662, 135, 250, 52, 37, { bold: true, color: C.teal });
text(s, '2 / 152 newly triggered\nPIN lockouts', 662, 195, 250, 57, 21);
table(s, [
  ['Recorded Denial Condition', 'Count'],
  ['Incorrect PIN', '8'],
  ['Consecutive-entry state violation', '5'],
  ['New PIN lockout', '2'],
  ['QR mismatch', '2'],
  ['Irregular exit state', '1'],
], 32, 269, 520, 182, [445, 75], 20);
text(s, 'Expected outcomes came from the researcher. Demonstrations contribute to denial and lockout counts.', 582, 285, 330, 105, 23);
foot(s, 'Controlled credential decisions do not establish physical identity or universal attack resistance.');
notes(s, '119-123', 'Accuracy denominator is 210 expected-valid entry grants plus 12 expected-invalid credential rejections: eight PIN failures, two newly triggered lockouts, and two QR mismatches. Five pre-authentication entry-state violations, one irregular exit, and 210 granted reset exits are excluded. Correct denials count as correct credential decisions. Mode accuracy is 70/70 Fast, 79/79 Standard, and 73/73 High-Security. Denial proportion uses all 438 records. PIN denominator is 140 successful PIN stages plus eight failure submissions plus two triggering lockout submissions plus two correct PIN stages followed by QR denial. The researcher confirmed every PIN submission was captured and both lockouts were newly triggered on the third incorrect submission. Lockout demos were researcher-led. Do not infer normal-user PIN usability or zero hardware/database failures. No hardware-read/database-timeout code appears among these 18 denials, but missed scans/cancellations were not independently audited.');

s = base('Manual and System Student Survey Ratings', 'III. RESULTS  ISO/IEC 25010');
chart(s, ['Functional\nSuitability', 'Performance\nEfficiency', 'Usability', 'Reliability', 'Security'], [
  { name: 'Manual, n = 70', values: [2.483, 1.932, 2.637, 2.183, 2.183], fill: C.gold },
  { name: 'System, n = 70', values: [3.763, 3.743, 3.740, 3.711, 3.751], fill: C.teal },
], { left: 32, top: 130, width: 895, height: 292 }, { yAxis: { min: 0, max: 4, majorUnit: 1, title: 'Mean rating on a 1-4 scale' }, dataLabels: { numberFormatCode: '0.00', textStyle: { typeface: 'Calibri', fontSize: 17, bold: true, fill: C.navy } } });
text(s, 'Largest descriptive difference: Performance Efficiency, 1.811 points.', 38, 434, 865, 27, 23, { bold: true, color: C.teal });
foot(s, 'Different student groups. Category means are descriptive. Only the overall score received a t-test.', 475);
notes(s, '135-139, 153-159', 'Full-precision displayed category means: manual 2.483, 1.932, 2.637, 2.183, 2.183; system 3.763, 3.743, 3.740, 3.711, 3.751. System composite sample SDs: 0.318, 0.366, 0.329, 0.348, 0.341. Manual composite sample SDs: 0.899, 0.919, 0.854, 0.852, 0.883. Difference column uses unrounded values, so subtracting displayed rounded category values can differ by 0.001. Manual Usability mean 2.637 is Satisfactory. Other manual category means are Unsatisfactory. All system category means are Highly Satisfactory. Shared category labels do not establish identical measurement because wording is process-specific.');

s = base('Proposed-System Evaluation by Group', 'III. RESULTS  ISO/IEC 25010');
chart(s, ['Students\nn = 70', 'Security Personnel\nn = 5', 'Administrative Group\nn = 5'], [{ name: 'Overall equal-category mean', values: [3.742, 3.634, 3.81375], fill: C.teal }], { left: 32, top: 135, width: 505, height: 288 }, { yAxis: { max: 4, majorUnit: 1, title: 'Mean rating' } });
text(s, 'Highly Satisfactory', 574, 144, 335, 34, 26, { bold: true, color: C.teal });
text(s, 'All three overall means fall in the 3.26-4.00 interpretation range.', 574, 188, 335, 68, 23);
text(s, 'Security personnel: five categories\nLowest category mean: Performance Efficiency, 3.45.', 574, 270, 335, 78, 22);
text(s, 'Admin: eight categories\nPortability 3.92\nPerformance Efficiency 3.68', 574, 365, 335, 78, 22);
foot(s, 'Role-specific instruments remain separate. No pooled system-wide mean or composite SD for the five-person groups.');
notes(s, '139-153, 186', 'Student mean 3.742 and composite sample SD 0.290. Security mean 3.634 rounds to 3.63. Category means are Functional Suitability 3.84, Performance Efficiency 3.45, Usability 3.60, Reliability 3.48, Security 3.80. Administrative mean 3.81375 rounds to 3.81. Category means are Functional Suitability 3.80, Performance Efficiency 3.68, Compatibility 3.75, Usability 3.88, Reliability 3.76, Security 3.84, Maintainability 3.88, Portability 3.92. Only item-frequency totals were available for the two groups of five, so respondent composite SDs cannot be recovered. Agreement ratings are perceived quality, not formal ISO certification or proof of technical review. Do not combine the differently sized and differently worded instruments into a pooled 3.81 score.');

s = base('Welch Test of Overall Survey Scores', 'III. RESULTS  MANUAL AND SYSTEM COMPARISON');
table(s, [
  ['Group', 'n', 'Overall Mean', 'Composite Sample SD'],
  ['Manual process', '70', '2.284', '0.753'],
  ['Proposed system', '70', '3.742', '0.290'],
], 32, 127, 895, 133, [320, 85, 220, 270], 23);
text(s, 'Rounded demonstration', 38, 282, 520, 29, 23, { bold: true, color: C.teal });
text(s, 'SE = sqrt(0.290^2 / 70 + 0.753^2 / 70) = 0.096445\nt = (3.742 - 2.284) / 0.096445 = 15.12', 38, 325, 865, 62, 22);
text(s, 't(89.076) = 15.124, p < .001', 38, 404, 470, 37, 29, { bold: true, color: C.teal });
text(s, 'Difference 1.458 points\n95% CI [1.267, 1.650]', 555, 399, 352, 62, 24);
foot(s, 'Reject H0 at alpha = .05. The result concerns overall survey ratings, not processing speed.', 478);
notes(s, '160-163', 'Final statistics use unrounded respondent-level equal-category scores. Rounded substitution illustrates the method and yields 15.12, while full precision yields t = 15.1241767182 and Welch df = 89.0760178841. Two-sided p is approximately 2.46554e-26, reported p < .001. The observed mean difference is 1.458 points with 95% CI 1.267 to 1.650. Reject H0 of equal population mean overall ratings. Different groups and process-specific wording limit causal attribution to the attendance method alone. The result does not establish objective security superiority, faster complete tasks, or performance improvement for each category.');

s = base('Discussion and Study Limitations', 'IV. DISCUSSION');
paragraphs(s, [
  ['Recorded capability', 'The prototype applied configured rules in the recorded tests. Logical re-entry rejection does not establish physical tailgating prevention.'],
  ['Timing interpretation', 'Millisecond internal latency and seconds of PIN/QR interaction describe different components. Complete-task speed and matched throughput remain unmeasured.'],
  ['Evaluation limits', 'Shared cards, fixed mode order, non-probability sampling, small staff groups, and different student survey groups limit generalization and causal interpretation.'],
], 132, 110, 880, 23);
notes(s, '163-187', 'SOP 1-4 receive evidence of integrated prototype functions and selected rule handling. SOP 5 concerns favorable perceived quality within the respondent groups. SOP 6A and 6B remain descriptive because measurement boundaries and rate denominators differ. SOP 6C has a significant overall survey comparison. Do not infer fraud reduction, exclusive credential ownership, queue reduction, workload savings, or universal offline reliability. Researcher-labelled outcomes are not blinded independent accuracy assessment. New features added after the observation require separate evaluation.');

s = base('Conclusion', 'IV. DISCUSSION', true);
paragraphs(s, [
  ['Developed prototype', 'The system combines configured NFC/PIN/QR workflows, eligibility checks, attendance records, and role-based monitoring.'],
  ['Supported findings', 'Controlled credential decisions matched expected outcomes. Respondents reported favorable perceived quality and higher overall system-group student ratings.'],
  ['Scope of the conclusion', 'The study supports demonstrated prototype functionality and the survey comparison. Deployment-scale performance and broader security effectiveness require further testing.'],
], 138, 110, 880, 25, true);
notes(s, '183-187', 'Keep the conclusion bounded to supplied observations and surveys. Say the prototype demonstrated selected functionality under controlled conditions. Do not use proven efficiency, strict physical control, best thesis, total security, no errors, or complete offline resilience. The statistically significant survey result does not remove the independent-group limitation or establish a matched processing-time improvement.');

s = base('Recommendations for Further Evaluation', 'IV. DISCUSSION', true);
paragraphs(s, [
  ['Equivalent timing and larger studies', 'Measure full tasks and sustained throughput with common boundaries. Use matched or clearly independent groups and counterbalance mode order.'],
  ['Security, recovery, and personnel workflows', 'Test credential sharing and copied payloads, outages and power recovery, alert workload, and routine PIN usability separately from demonstrations.'],
  ['Documentation and deployment readiness', 'Retain anonymized raw responses, complete dated evidence, and current interface screenshots. Resolve retention/destruction scope before wider deployment.'],
], 138, 110, 880, 24, true);
notes(s, '188-191', 'Further recommendations include larger multi-terminal studies, broader colleges and staff samples, individual credential assignment when testing identity-linked behavior, and independent audits of record quality and personnel workload. Test later signed-QR credentials, durable queues, event completion, and graduation clearance separately. Document failure, cancellation, retry, and synchronization outcomes with clear denominators. Visitor modules are future work, not evaluated results. Pending same-participant and documentation requirements remain in the pre-oral matrix.');

// Keep the supplied closing artwork while turning it into a demonstration and Q&A slide.
ordered.push(demo.id.startsWith('sl/') ? demo.id : `sl/${demo.id}`);
titles.push('System Demonstration and Questions');
p.delete(record(17, 'PROTOTYPE DEMONSTRATION').id);
text(demo, 'SYSTEM DEMONSTRATION', 130, 156, 700, 35, 21, { bold: true, align: 'center', color: '#5EC4CE', font: 'Inter' });
text(demo, 'Live Demo and Questions', 115, 217, 730, 58, 40, { align: 'center', color: '#FFFFFF', font: 'Inter' });
text(demo, 'Configured modes, entry/exit timestamps, alerts, and authorized local cleanup', 157, 312, 646, 62, 23, { align: 'center', color: '#C9D8EC' });
p.resolve(record(17, '18 /').id).text = '24 / 24';
notes(demo, '101-105, 205-212', 'Demonstrate the current system separately from the historical August results. Use a prepared valid credential for each mode, then show a documented invalid condition without describing all denials as failures. Show timestamps and the result-retention duration. For administrative cleanup, use a disposable test database or show the authorization and confirmation without deleting real research data. Explain that local log cleanup retains cloud history. Identify later additions as current functionality requiring separate evaluation.');

if (ordered.length !== 24) throw new Error(`Expected 24 slides, got ${ordered.length}`);
p.slides.reorder(ordered);
for (const slide of p.slides.items) slide.hidden = false;
await fs.mkdir(path.join(BUILD, 'draft'), { recursive: true });
await fs.mkdir(path.dirname(FINAL), { recursive: true });
await fs.writeFile(path.join(BUILD, 'slide-outline.json'), JSON.stringify(titles, null, 2));
await fs.writeFile(path.join(BUILD, 'authored-proto.json'), JSON.stringify(p.toProto()));
const candidate = path.join(BUILD, 'draft/candidate.pptx');
await (await PresentationFile.exportPptx(p)).save(candidate);
console.log('Exported candidate with', p.slides.items.length, 'slides');
const beforeHash = createHash('sha256').update(await fs.readFile(SOURCE)).digest('hex');
const result = await finalizePresentation({
  workspaceDir: ROOT,
  candidatePath: candidate,
  finalPath: FINAL,
  pythonExecutable: PYTHON,
  integrityValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_package_integrity.py'),
  layoutValidatorPath: path.join(SKILL, 'container_tools/inspect_presentation_layout_geometry.py'),
  layoutArgs: ['--expected-slide-size-emu', '9144000,5143500', '--validate-heading-fit', ...[...new Set(tableOwners)].flatMap(n => ['--require-native-table-slide', String(n)])],
  explicitTotalSlideCount: 24,
  requiredNativeTableOwnerSlides: [...new Set(tableOwners)],
  requiredNativeChartOwnerSlides: [...new Set(chartOwners)],
  materializeLiteralChartWorkbooks: true,
  fontPolicy: { basis: 'reference', families: ['Calibri', 'Cambria', 'Inter', 'Inter ExtraBold', 'Inter SemiBold'], referencePath: SOURCE, referenceSha256: beforeHash },
  verifyArtifactToolImport: true,
  receiptPath: path.join(BUILD, `${path.basename(FINAL)}.validation.json`),
});
console.log('Final deck:', result.finalPath, 'Layout warnings:', result.presentationLayout?.warning_count);
const previewDir = path.join(BUILD, 'final-render');
await fs.mkdir(previewDir, { recursive: true });
for (const [i, slide] of p.slides.items.entries()) {
  const png = await p.export({ slide, format: 'png', scale: 1.5 });
  await fs.writeFile(path.join(previewDir, `slide-${String(i + 1).padStart(2, '0')}.png`), new Uint8Array(await png.arrayBuffer()));
  console.log('Rendered revised slide', i + 1);
}
