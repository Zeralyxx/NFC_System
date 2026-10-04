import fs from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const data = JSON.parse(await fs.readFile(new URL('./analysis.json', import.meta.url), 'utf8'));
const out = new URL('./', import.meta.url);
const combined = process.argv.includes('--combined');
const manualData = combined ? JSON.parse(await fs.readFile(new URL('./manual.json', import.meta.url), 'utf8')) : null;
const wb = Workbook.create();
const summary = wb.worksheets.add('Summary');
const measurements = wb.worksheets.add('Measurements');
const raw = wb.worksheets.add('RawRecords');
const manual = combined ? wb.worksheets.add('ManualBaseline') : null;
const modes = ['Fast', 'Standard', 'HighSecurity'];
const colors = { ink: '#243B40', teal: '#146C78', light: '#E9F2F3', gold: '#F5E7BA' };
// Numeric Excel serials preserve millisecond precision during date export.
const serialDate = value => Date.parse(value.replace(' ', 'T') + 'Z') / 86400000 + 25569;
function base(sheet, range) {
  sheet.showGridLines = false;
  sheet.getRange(range).format.font = { name: 'Arial', size: 10, color: colors.ink };
  sheet.getRange(range).format.rowHeight = 23;
  sheet.getRange(range).format.columnWidth = 17;
}
function band(sheet, row, text, end = 'H') {
  const range = sheet.getRange(`A${row}:${end}${row}`);
  range.merge();
  range.values = [[text]];
  range.format.fill = colors.teal;
  range.format.font = { name: 'Arial', size: 12, bold: true, color: '#FFFFFF' };
  range.format.rowHeight = 30;
}
function header(sheet, row, values) {
  const range = sheet.getRangeByIndexes(row - 1, 0, 1, values.length);
  range.values = [values];
  range.format.fill = colors.light;
  range.format.font.bold = true;
  range.format.wrapText = true;
  range.format.rowHeight = 44;
}
function note(sheet, row, text, end = 'H') {
  const range = sheet.getRange(`A${row}:${end}${row}`);
  range.merge(); range.values = [[text]];
  range.format.wrapText = true;
  range.format.rowHeight = 35;
}
base(summary, 'A1:H80'); base(measurements, 'A1:M260'); base(raw, combined ? 'A1:Y450' : 'A1:U450');
summary.getRange('B7:H40').format.horizontalAlignment = 'right';
summary.getRange('A:A').format.columnWidth = 25;
summary.freezePanes.freezeRows(4);
band(summary, 1, 'Chapter 4 | System observation metrics');
note(summary, 2, '12-13 August 2026 | 40 + 30 reported participants | 70 successful entry trials per mode');
note(summary, 3, combined ? 'Researcher-labelled credential outcomes and 70 manual writing durations added. Timing boundaries differ; no matched speed-improvement ratio is claimed.' : 'No manual comparison is included. Denial intent is pending confirmation; grants are not a measure of accuracy.');

band(raw, 1, 'Original export records with separate analysis metadata', 'U');
note(raw, 2, `Batch 1 source: ${data.sources[0].path}`, 'U');
note(raw, 3, `Batch 2 source: ${data.sources[1].path}`, 'U');
note(raw, 4, 'A:O preserve original field values. P:U are added provenance/classification fields. Names identify reused card profiles, not unique participants.', 'U');
header(raw, 6, [...data.headers, 'Source batch', 'Source sheet', 'Source Excel row', 'Record category', 'Entry round', 'Audit note', ...(combined ? ['Applicable credential decision', 'Recorded PIN submission', 'Expected credential decision', 'Correct credential decision'] : [])]);
const anomalyKeys = new Set(data.component_anomalies.map(r => `${r.batch}|${r.sheet}|${r.row}`));
const sourceIndex = new Map();
const rawRows = data.records.map((r, i) => {
  sourceIndex.set(`${r.batch}|${r.sheet}|${r.row}`, i + 7);
  const eligible = r.category === 'Successful entry' || (r.values[2] === 'Entry' && ['DENIED [PIN_FAILURE]', 'DENIED [PIN_LOCKED]', 'DENIED [QR_REPLACED_OR_MISMATCH]'].includes(r.values[4]));
  const pin = eligible && r.values[3] !== 'Fast';
  return [...r.values.map((v, col) => col === 0 ? serialDate(v) : v),
    r.batch, r.sheet, r.row, r.category, r.round,
    anomalyKeys.has(`${r.batch}|${r.sheet}|${r.row}`) ? 'PIN_LOCKED: totals include PIN time hidden as N/A in original component fields.' : '',
    ...(combined ? [Number(eligible), Number(pin), eligible ? (r.category === 'Successful entry' ? 'GRANTED' : 'DENIED') : 'Not applicable', null] : [])];
});
raw.getRange(combined ? 'A7:Y444' : 'A7:U444').values = rawRows;
if (combined) {
  raw.getRange('Y7:Y444').formulas = data.records.map((r, i) => [`=IF(V${i+7}=1,IF(E${i+7}="GRANTED",IF(X${i+7}="GRANTED",1,0),IF(X${i+7}="DENIED",1,0)),0)`]);
  raw.getRange('V:Y').format.columnWidth = 28;
  note(raw, 5, 'Expected labels are based on researcher confirmation of valid grants and deliberate invalid conditions, not an independent blinded assessment. Pre-authentication state denials and reset exits are excluded from credential accuracy.', 'Y');
}
raw.getRange('A7:A444').setNumberFormat('yyyy-mm-dd hh:mm:ss.000');
raw.getRange('F7:M444').setNumberFormat('0.0000');
raw.getRange('F7:M444').format.horizontalAlignment = 'right';
raw.getRange('A:A').format.columnWidth = 27;
raw.getRange('B:B').format.columnWidth = 25;
raw.getRange('E:E').format.columnWidth = 40;
raw.getRange('N:O').format.columnWidth = 35;
raw.getRange('Q:Q').format.columnWidth = 25;
raw.getRange('S:S').format.columnWidth = 23;
raw.getRange('U:U').format.columnWidth = 62;
raw.getRange('U7:U444').format.wrapText = true;
raw.freezePanes.freezeRows(6);
for (let i = 0; i < data.records.length; i++) {
  if (data.records[i].category === 'Denied record') raw.getRange(`A${i + 7}:U${i + 7}`).format.fill = '#FFF4D8';
}

band(measurements, 1, 'Entry runs and eligible successful-entry measurements', 'M');
note(measurements, 2, 'Run span begins at the first recorded grant and ends at the last. Intervals = grants - 1; reset exits and between-run explanations are excluded.', 'M');
note(measurements, 3, 'Within-run demonstrations/pauses cannot be removed from elapsed spans using verdict filtering. Rate is not full physical processing capacity.', 'M');
header(measurements, 5, ['Batch', 'Mode', 'Round', 'Granted entries', 'First recorded grant', 'Last recorded grant', 'Intervals', 'Span (s)', 'Spacing (s)', 'Rate (/min)', 'Denials within span']);
for (let i = 0; i < data.runs.length; i++) {
  const r = data.runs[i], row = i + 6;
  measurements.getRange(`A${row}:F${row}`).values = [[r.batch, r.mode, r.round, r.n,
    serialDate(r.first), serialDate(r.last)]];
  measurements.getRange(`G${row}:J${row}`).formulas = [[`=D${row}-1`, `=(F${row}-E${row})*86400`, `=H${row}/G${row}`, `=60*G${row}/H${row}`]];
  measurements.getRange(`K${row}`).values = [[r.denied_within]];
}
measurements.getRange('E6:F17').setNumberFormat('yyyy-mm-dd hh:mm:ss.000');
measurements.getRange('E:F').format.columnWidth = 28;
measurements.getRange('H6:J17').setNumberFormat('0.000');
measurements.getRange('B6:B17').format.horizontalAlignment = 'center';
band(measurements, 20, 'Successful entries only | all timing fields below are milliseconds', 'M');
note(measurements, 21, 'Only granted Entry rows are eligible. Slow observations are retained. N/A is preserved and never interpreted as zero.', 'M');
header(measurements, 22, ['Batch', 'Mode', 'Timestamp', 'NFC system', 'PIN user', 'PIN system', 'QR user', 'QR system', 'Total user', 'Total system', 'DB query', 'Source Excel row', 'RawRecords row']);
const selected = modes.flatMap(mode => data.records.filter(r => r.values[3] === mode && r.category === 'Successful entry'));
for (let i = 0; i < selected.length; i++) {
  const r = selected[i], row = i + 23, source = sourceIndex.get(`${r.batch}|${r.sheet}|${r.row}`);
  measurements.getRange(`A${row}:B${row}`).values = [[r.batch, r.values[3]]];
  const cols = ['A', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M'];
  measurements.getRange(`C${row}:K${row}`).formulas = [cols.map(c => `=RawRecords!${c}${source}`)];
  measurements.getRange(`L${row}:M${row}`).values = [[r.row, source]];
}
measurements.getRange('C23:C232').setNumberFormat('yyyy-mm-dd hh:mm:ss.000');
measurements.getRange('C:C').format.columnWidth = 28;
measurements.getRange('D23:K232').setNumberFormat('0.0000');
measurements.freezePanes.freezeRows(22);

band(summary, 5, 'Record reconciliation');
header(summary, 6, ['Mode', 'Successful entries', 'Reset exits', 'Denied records', 'Total records', 'Batch 1 entries', 'Batch 2 entries', 'Card profiles']);
for (let i = 0; i < 3; i++) {
  const row = 7 + i, mode = modes[i];
  summary.getRange(`A${row}`).values = [[mode]];
  summary.getRange(`B${row}:G${row}`).formulas = [[
    ...['Successful entry', 'Reset exit', 'Denied record'].map(c => `=COUNTIFS(RawRecords!$D$7:$D$444,A${row},RawRecords!$S$7:$S$444,"${c}")`),
    `=SUM(B${row}:D${row})`,
    ...[1, 2].map(b => `=COUNTIFS(RawRecords!$D$7:$D$444,A${row},RawRecords!$S$7:$S$444,"Successful entry",RawRecords!$P$7:$P$444,${b})`)
  ]];
  summary.getRange(`H${row}`).values = [[20]];
}
summary.getRange('A10').values = [['Total']];
summary.getRange('B10:G10').formulas = [[...['B', 'C', 'D', 'E', 'F', 'G'].map(c => `=SUM(${c}7:${c}9)`)]];
summary.getRange('A10:H10').format.fill = colors.gold;
note(summary, 11, '210 entry trials represent 70 reported people tested in three modes, not 210 unique people. Twenty card profiles were reused.');

band(summary, 13, 'Measured internal latency | successful entries only');
header(summary, 14, ['Mode', 'n', 'Mean system (ms)', 'Median system (ms)', 'Sample SD (ms)', 'Mean DB (ms)', 'Minimum system (ms)', 'Maximum system (ms)']);
for (let i = 0; i < 3; i++) {
  const row = 15 + i, first = 23 + 70 * i, last = first + 69;
  const sys = `Measurements!J${first}:J${last}`, db = `Measurements!K${first}:K${last}`;
  summary.getRange(`A${row}`).values = [[modes[i]]];
  summary.getRange(`B${row}:H${row}`).formulas = [[`=COUNT(${sys})`, `=AVERAGE(${sys})`, `=MEDIAN(${sys})`, `=STDEV(${sys})`, `=AVERAGE(${db})`, `=MIN(${sys})`, `=MAX(${sys})`]];
}
summary.getRange('C15:H17').setNumberFormat('0.00');
note(summary, 18, 'DB query time overlaps internal component timing. Do not add DB time to total system latency. These are not full student transaction times.');

band(summary, 20, 'Recorded PIN/QR interaction | successful entries only');
header(summary, 21, ['Mode', 'n timed', 'Mean PIN (s)', 'Mean QR (s)', 'Mean total (s)', 'Median total (s)', 'Sample SD (s)', 'Maximum total (s)']);
for (let i = 0; i < 3; i++) {
  const row = 22 + i, first = 23 + 70 * i, last = first + 69;
  summary.getRange(`A${row}`).values = [[modes[i]]];
  const total = `Measurements!I${first}:I${last}`;
  summary.getRange(`B${row}`).formulas = [[`=COUNT(${total})`]];
  if (i === 0) summary.getRange(`C${row}:H${row}`).values = [['N/A', 'N/A', 'N/A', 'N/A', 'N/A', 'N/A']];
  else {
    summary.getRange(`C${row}`).formulas = [[`=AVERAGE(Measurements!E${first}:E${last})/1000`]];
    if (i === 1) summary.getRange(`D${row}`).values = [['N/A']];
    else summary.getRange(`D${row}`).formulas = [[`=AVERAGE(Measurements!G${first}:G${last})/1000`]];
    summary.getRange(`E${row}:H${row}`).formulas = [[`=AVERAGE(${total})/1000`, `=MEDIAN(${total})/1000`, `=STDEV(${total})/1000`, `=MAX(${total})/1000`]];
  }
}
summary.getRange('C22:H24').setNumberFormat('0.000');
note(summary, 25, 'Fast bypasses PIN/QR timing; N/A does not mean instantaneous entry. User timers omit some approach, prompt, transition and logging time.');

band(summary, 27, 'Recorded-entry spacing | not stopwatch throughput');
header(summary, 28, ['Mode', 'Entry runs', 'Intervals', 'Summed span (s)', 'Mean spacing (s)', 'Rate (/min)', 'Within-span denials', 'Scope']);
for (let i = 0; i < 3; i++) {
  const row = 29 + i;
  summary.getRange(`A${row}`).values = [[modes[i]]];
  summary.getRange(`B${row}:G${row}`).formulas = [[
    `=COUNTIF(Measurements!$B$6:$B$17,A${row})`,
    `=SUMIF(Measurements!$B$6:$B$17,A${row},Measurements!$G$6:$G$17)`,
    `=SUMIF(Measurements!$B$6:$B$17,A${row},Measurements!$H$6:$H$17)`,
    `=D${row}/C${row}`, `=60*C${row}/D${row}`,
    `=SUMIF(Measurements!$B$6:$B$17,A${row},Measurements!$K$6:$K$17)`
  ]];
  summary.getRange(`H${row}`).values = [['4 entry runs']];
}
summary.getRange('D29:F31').setNumberFormat('0.000');
note(summary, 32, '66 observed intervals per mode: 19 + 19 + 19 + 9. Rate = 60 x summed intervals / summed spans. First-to-last spans do not time all 70 complete transactions.');

band(summary, 34, 'Denied records | security outcomes, not accuracy');
header(summary, 35, ['Logged verdict', 'Count', 'Interpretation']);
summary.getRange('C35:H35').merge();
summary.getRange('C35:H40').format.horizontalAlignment = 'left';
let denialRow = 36;
const denialLabels = {
  'PIN_FAILURE': 'PIN failure',
  'ANTI_TAILGATING_VIOLATION': 'Anti-tailgating violation',
  'PIN_LOCKED': 'PIN locked',
  'QR_REPLACED_OR_MISMATCH': 'QR replacement / mismatch',
  'IRREGULAR_EXIT_SEQUENCE': 'Irregular exit sequence'
};
for (const [code, label] of Object.entries(denialLabels)) {
  const verdict = `DENIED [${code}]`;
  summary.getRange(`A${denialRow}`).values = [[label]];
  summary.getRange(`B${denialRow}`).formulas = [[`=COUNTIF(RawRecords!$E$7:$E$444,"${verdict}")`]];
  summary.getRange(`C${denialRow}:H${denialRow}`).merge();
  summary.getRange(`C${denialRow}`).values = [[combined ? 'Researcher-confirmed expected invalid-condition rejection.' : 'Demonstration vs ordinary mistake not yet classified.']];
  denialRow++;
}
summary.getRange('A36:A40').format.wrapText = true;
summary.getRange('A36:H40').format.rowHeight = 35;
band(summary, 42, 'Reporting limits and provenance');
const notes = [
  combined ? 'Observed credential accuracy uses 222 researcher-labelled recorded decisions; it does not establish unique real-person identity or universal accuracy.' : 'Accuracy, false acceptance, false rejection and ordinary failure rates cannot be established without labelled expected outcomes and complete attempt counts.',
  'Exits were operator-assisted resets for card reuse; they are retained but excluded from the participant entry timing summaries.',
  'Modes were always Fast -> Standard -> HighSecurity. Practice and order effects were not controlled; results are descriptive, not causal.',
  'The logs use one device (Admin Office - 1). No concurrent multi-kiosk or campus-scale capacity claim is supported.',
  'Two PIN_LOCKED rows hide component PIN durations as N/A while keeping nonzero totals. Both are flagged in RawRecords; no successful-entry row has this mismatch.',
  combined ? 'Manual writing time, automated component timers and recorded-entry spacing have different boundaries. No matched end-to-end speed ratio is claimed.' : 'Manual time, throughput and unverified-enrollment proxies remain pending. Do not reuse old improvement ratios or an assumed 2.50-second baseline.',
  `Batch 1 SHA-256: ${data.sources[0].sha256}`,
  `Batch 2 SHA-256: ${data.sources[1].sha256}`,
  'Source records: 438; exact duplicate rows: 0; successful entry component totals reconcile. Statistical SD uses the sample definition (n - 1).'
];
notes.forEach((text, i) => note(summary, 43 + i, text));
if (combined) {
  base(manual, 'A1:F93');
  manual.getRange('A:A').format.columnWidth = 38;
  manual.getRange('B:B').format.columnWidth = 25;
  band(manual, 1, 'Manual paper attendance baseline', 'F');
  note(manual, 2, `Source: ${manualData.source}`, 'F');
  note(manual, 3, 'Timing: first written character to complete name, course and section, student ID and signature. No ID or enrollment validation.', 'F');
  note(manual, 4, '70 writing durations. A summed service-time rate is not measured continuous queue throughput. Cohort linkage and test dates are not assumed.', 'F');
  header(manual, 6, ['Measure', 'Result']);
  const labels = ['Observations', 'Sum writing duration (s)', 'Mean writing duration (s)', 'Median writing duration (s)', 'Sample SD (s)', 'Minimum (s)', 'Maximum (s)', 'Service-equivalent rate (/min)', 'Enrollment checks performed'];
  manual.getRange('A7:A15').values = labels.map(label => [label]);
  manual.getRange('B7:B14').formulas = [['=COUNT(B20:B89)'],['=SUM(B20:B89)'],['=AVERAGE(B20:B89)'],['=MEDIAN(B20:B89)'],['=STDEV(B20:B89)'],['=MIN(B20:B89)'],['=MAX(B20:B89)'],['=60/B9']];
  manual.getRange('B15').values = [[0]];
  manual.getRange('B8:B14').setNumberFormat('0.000');
  note(manual, 17, `Source SHA-256: ${manualData.sha256}`, 'F');
  header(manual, 19, ['Respondent number', 'Writing duration (s)']);
  manual.getRange('A20:B89').values = manualData.observations.map(r => [r.respondent, r.seconds]);
  manual.getRange('B20:B89').setNumberFormat('0.00');
  manual.freezePanes.freezeRows(19);
  band(summary, 54, 'Chapter 3 metrics and manual baseline');
  header(summary, 55, ['Metric', '', 'Result', 'Scope']);
  summary.getRange('A55:B55').merge(); summary.getRange('D55:H55').merge();
  const metrics = [
    ['Observed credential-decision accuracy','=SUM(RawRecords!$Y$7:$Y$444)/SUM(RawRecords!$V$7:$V$444)','222 labelled applicable decisions; excludes exits and pre-authentication state denials.','0.00%'],
    ['Recorded PIN submissions','=SUM(RawRecords!$W$7:$W$444)','Includes successful PIN stages, failed submissions, triggering lockouts and PIN passed before QR denial.','0'],
    ['Intentional lockout triggers','=COUNTIF(RawRecords!$E$7:$E$444,"DENIED [PIN_LOCKED]")','Researcher confirmed newly triggered third-wrong-PIN events, not repeat locked-card scans.','0'],
    ['Recorded PIN lockout rate','=C58/C57','Every individual PIN submission captured, according to researcher confirmation.','0.00%'],
    ['Recorded transaction denial rate','=D10/E10','All 438 records; includes reset exits and intentional invalid attempts.','0.00%'],
    ['Mean manual writing time (s)','=ManualBaseline!B9','First character to completion of required paper-attendance fields.','0.000'],
    ['Manual service-equivalent rate','=ManualBaseline!B14','Hypothetical continuous writing service; not measured queue throughput.','0.00'],
    ['Manual enrollment check omission','=(ManualBaseline!B7-ManualBaseline!B15)/ManualBaseline!B7','No ID/enrollment validation in the stated simulation; not a false-enrollment rate.','0.00%'],
    ['Full automated processing time','Not established','No end-to-end NFC-tap-to-finished-verification timestamps.','@'],
    ['Complete-round throughput','Not established','No complete observation-period start and finish.','@'],
    ['Matched speed-improvement ratio','Not established','Manual event writing and automated entry verification use different boundaries.','@'],
  ];
  for (let i=0;i<metrics.length;i++) {
    const row=56+i, [label,value,scope,format]=metrics[i];
    summary.getRange(`A${row}:B${row}`).merge(); summary.getRange(`A${row}`).values=[[label]];
    if (value.startsWith('=')) summary.getRange(`C${row}`).formulas=[[value]];
    else summary.getRange(`C${row}`).values=[[value]];
    summary.getRange(`C${row}`).setNumberFormat(format);
    summary.getRange(`D${row}:H${row}`).merge(); summary.getRange(`D${row}`).values=[[scope]];
    summary.getRange(`A${row}:H${row}`).format.wrapText=true;
    summary.getRange(`A${row}:H${row}`).format.rowHeight=37;
  }
  band(summary, 69, 'Recorded authentication and PIN attempt audit');
  header(summary, 70, ['Mode','Valid grants','Credential denials','Auth decisions','Correct decisions','PIN submissions','Lockout triggers','Lockout rate']);
  for (let i=0;i<3;i++) {
    const row=71+i;
    summary.getRange(`A${row}`).values=[[modes[i]]];
    summary.getRange(`B${row}:H${row}`).formulas=[[
      `=COUNTIFS(RawRecords!$D$7:$D$444,A${row},RawRecords!$S$7:$S$444,"Successful entry")`,
      `=COUNTIFS(RawRecords!$D$7:$D$444,A${row},RawRecords!$V$7:$V$444,1,RawRecords!$S$7:$S$444,"Denied record")`,
      `=SUMIF(RawRecords!$D$7:$D$444,A${row},RawRecords!$V$7:$V$444)`,
      `=SUMIF(RawRecords!$D$7:$D$444,A${row},RawRecords!$Y$7:$Y$444)`,
      `=SUMIF(RawRecords!$D$7:$D$444,A${row},RawRecords!$W$7:$W$444)`,
      `=COUNTIFS(RawRecords!$D$7:$D$444,A${row},RawRecords!$E$7:$E$444,"DENIED [PIN_LOCKED]")`,
      `=IF(F${row}=0,"N/A",G${row}/F${row})`
    ]];
  }
  summary.getRange('H71:H73').setNumberFormat('0.00%');
  note(summary, 75, 'All 18 denials were reported as expected invalid-condition outcomes. Six state-rule denials are separate from the 222 applicable credential decisions.');
}
wb.recalculate();
console.log((await wb.inspect({ kind: 'region', sheetId: 'Summary', range: 'A6:H31', maxChars: 7000, tableMaxRows: 26, tableMaxCols: 8 })).ndjson);
await (await SpreadsheetFile.exportXlsx(wb)).save(fileURLToPath(new URL(combined ? 'Chapter-4-Combined-Metrics.xlsx' : 'Chapter-4-System-Metrics.xlsx', out)));
const preview = await wb.render({ sheetName: 'Summary', range: 'A1:H32', scale: 1.4, format: 'png' });
await fs.writeFile(new URL('workbook-preview.png', out), new Uint8Array(await preview.arrayBuffer()));
for (const [sheetName, range, name] of [
  ['Measurements', 'A1:K17', 'measurements-preview.png'],
  ['RawRecords', 'A4:M12', 'raw-records-preview.png'],
  ['Summary', 'A34:H51', 'notes-preview.png']
]) {
  const image = await wb.render({ sheetName, range, scale: 1.3, format: 'png' });
  await fs.writeFile(new URL(name, out), new Uint8Array(await image.arrayBuffer()));
}
if (combined) {
  for (const [sheetName,range,name] of [['Summary','A54:H75','combined-summary-preview.png'],['ManualBaseline','A1:F30','manual-baseline-preview.png']]) {
    const preview=await wb.render({sheetName,range,scale:1.3,format:'png'});
    await fs.writeFile(new URL(name,out),new Uint8Array(await preview.arrayBuffer()));
  }
}
console.log('Exported Chapter-4-System-Metrics.xlsx and summary preview.');
