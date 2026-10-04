const fs = require('fs');
const path = require('path');
const runtime = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
const { instance } = require(path.join(runtime, '@viz-js/viz'));
const sharp = require(path.join(runtime, 'sharp'));
const repo = path.resolve(__dirname, '../..');
const schema = new Map();

function splitColumns(text) {
  let depth = 0, quote = false, start = 0;
  const parts = [];
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === "'") quote = !quote;
    if (!quote) {
      if (c === '(') depth++;
      if (c === ')') depth--;
      if (c === ',' && depth === 0) { parts.push(text.slice(start, i).trim()); start = i + 1; }
    }
  }
  parts.push(text.slice(start).trim());
  return parts;
}

for (const file of fs.readdirSync(path.join(repo, 'NFC_System')).filter(n => /^DatabaseService.*\.cs$/.test(n))) {
  const source = fs.readFileSync(path.join(repo, 'NFC_System', file), 'utf8');
  const ddl = /CREATE TABLE IF NOT EXISTS\s+(\w+)\s*\(([\s\S]*?)\)\s*(?:ENGINE=InnoDB)?;/g;
  for (const match of source.matchAll(ddl)) {
    const columns = [], composite = [], unique = [];
    for (const part of splitColumns(match[2])) {
      const pk = part.match(/^PRIMARY KEY\s*\(([^)]+)\)/i);
      const uq = part.match(/^UNIQUE KEY\s+\w+\s*\(([^)]+)\)/i);
      if (pk) { composite.push(...pk[1].split(',').map(x => x.trim())); continue; }
      if (uq) { unique.push(uq[1]); continue; }
      if (/^(INDEX|KEY|CONSTRAINT)\b/i.test(part)) continue;
      const col = part.match(/^(\w+)\s+([A-Z]+(?:\([\d,]+\))?)\s*(.*)$/i);
      if (!col) throw new Error(`Cannot parse ${match[1]}: ${part}`);
      columns.push({ name: col[1], type: col[2], pk: /PRIMARY KEY/i.test(col[3]), uq: /UNIQUE/i.test(col[3]), nn: /NOT NULL|PRIMARY KEY/i.test(col[3]), auto: /AUTO_INCREMENT/i.test(col[3]) });
    }
    for (const col of columns) if (composite.includes(col.name)) { col.pk = true; col.nn = true; }
    schema.set(match[1], { columns, unique });
  }
}

const add = (table, name, type, extra = {}) => schema.get(table).columns.push({ name, type, nn: true, ...extra });
for (const table of ['fast_mode_logs', 'standard_mode_logs', 'high_security_mode_logs', 'event_attendance', 'alerts']) {
  add(table, 'device_id', 'VARCHAR(32)'); add(table, 'device_name', 'VARCHAR(100)');
}
add('alerts', 'device_name_change_id', 'VARCHAR(32)', { nn: false, uq: true });
for (const [name, type, nn] of [
  ['graduation_clearance', 'VARCHAR(20)', true], ['graduation_reason', 'VARCHAR(1000)', true],
  ['graduation_changed_by', 'VARCHAR(150)', true], ['graduation_changed_at_utc', 'DATETIME(6)', false],
  ['graduation_revision', 'BIGINT', true]
]) add('students', name, type, { nn });
if (schema.size !== 23) throw new Error(`Expected 23 tables, found ${schema.size}`);

const escape = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const palettes = { core: '#DBEEE3', verification: '#F7E4BB', attendance: '#DDECF7', events: '#F6DDE0', security: '#E9E2F3', reference: '#EDF0F2' };
const sources = { students: ['student_id'], courses: ['course_name'], events: ['event_id'], attendance_devices: ['device_id'], attendance_receipts: ['transaction_id'], event_attendance: ['id'] };

function tableNode(name, group, refs, reference = false) {
  const data = schema.get(name);
  const columns = reference ? data.columns.filter(c => sources[name].includes(c.name)) : data.columns;
  const rows = columns.map(c => {
    const flags = [c.pk ? 'PK' : '', c.uq ? 'UQ' : '', refs.includes(c.name) ? 'LR' : '', c.auto ? 'AI' : '', c.nn ? 'NN' : 'NULL'].filter(Boolean).join(', ');
    return `<TR><TD PORT="${c.name}" ALIGN="LEFT">${escape(c.name)}</TD><TD ALIGN="LEFT">${escape(c.type)}</TD><TD ALIGN="LEFT"><FONT COLOR="#44515E">${flags}</FONT></TD></TR>`;
  }).join('');
  const foot = !reference && data.unique.length ? `<TR><TD COLSPAN="3" ALIGN="LEFT"><FONT POINT-SIZE="11">UQ (${escape(data.unique.join('; '))})</FONT></TD></TR>` : '';
  return `${name} [label=<<TABLE BORDER="1" COLOR="#89949E" CELLBORDER="0" CELLSPACING="0" CELLPADDING="6"><TR><TD COLSPAN="3" BGCOLOR="${palettes[reference ? 'reference' : group]}"><B>${name.toUpperCase()}</B>${reference ? '<BR/><FONT POINT-SIZE="11">REFERENCE ONLY</FONT>' : ''}</TD></TR>${rows}${foot}</TABLE>>];`;
}

const panels = [
  { id: '01-core', title: 'A. Core Records and Event Membership', group: 'core', tables: ['students', 'courses', 'events', 'event_approved_students', 'staff', 'app_settings'], references: [], edges: [
    ['courses', 'course_name', 'students', 'course', 'many'], ['students', 'student_id', 'event_approved_students', 'student_id', 'many'], ['events', 'event_id', 'event_approved_students', 'event_id', 'many']
  ] },
  { id: '02-verification', title: 'B. Verification Logs and Alerts', group: 'verification', tables: ['fast_mode_logs', 'standard_mode_logs', 'high_security_mode_logs', 'alerts'], references: ['students', 'attendance_devices'], edges: [
    ...['fast_mode_logs', 'standard_mode_logs', 'high_security_mode_logs', 'alerts'].flatMap(t => [['students', 'student_id', t, 'student_id', 'many'], ['attendance_devices', 'device_id', t, 'device_id', 'many']])
  ] },
  { id: '03-attendance', title: 'C. Campus Attendance and Transaction Synchronization', group: 'attendance', tables: ['attendance_devices', 'attendance_receipts', 'attendance_decisions', 'attendance_current', 'attendance_visits'], references: ['students'], edges: [
    ['students', 'student_id', 'attendance_current', 'student_id', 'one'], ['students', 'student_id', 'attendance_visits', 'student_id', 'many'],
    ['attendance_devices', 'device_id', 'attendance_receipts', 'device_id', 'many'], ['attendance_receipts', 'transaction_id', 'attendance_decisions', 'transaction_id', 'one'],
    ['attendance_receipts', 'transaction_id', 'attendance_visits', 'entry_transaction_id', 'many'], ['attendance_receipts', 'transaction_id', 'attendance_visits', 'exit_transaction_id', 'many']
  ] },
  { id: '04-events', title: 'D. Event Attendance, Roster Synchronization and Completion', group: 'events', tables: ['event_attendance', 'event_roster_sync', 'event_completion_policies', 'event_completion_scans', 'event_completion_audit'], references: ['students', 'events', 'attendance_devices', 'attendance_receipts'], edges: [
    ...['event_attendance', 'event_roster_sync', 'event_completion_scans', 'event_completion_audit'].flatMap(t => [['students', 'student_id', t, 'student_id', 'many'], ['events', 'event_id', t, 'event_id', 'many']]),
    ['events', 'event_id', 'event_completion_policies', 'event_id', 'one'],
    ...['event_attendance', 'event_completion_scans', 'event_completion_audit'].map(t => ['attendance_devices', 'device_id', t, 'device_id', 'many']),
    ['attendance_receipts', 'transaction_id', 'event_completion_scans', 'transaction_id', 'one'],
    ['event_attendance', 'id', 'event_completion_scans', 'event_log_id', 'many']
  ] },
  { id: '05-security', title: 'E. PIN Security and Graduation Audit', group: 'security', tables: ['pin_security_versions', 'pin_attempt_receipts', 'student_graduation_audit'], references: ['students'], edges: [
    ['students', 'student_id', 'pin_security_versions', 'student_id', 'one'], ['students', 'student_id', 'pin_attempt_receipts', 'student_id', 'many'], ['students', 'student_id', 'student_graduation_audit', 'student_id', 'many']
  ] }
];

async function main() {
  const viz = await instance();
  const panelFiles = [];
  for (const p of panels) {
    const vertical = ['02-verification', '04-events', '05-security'].includes(p.id);
    const all = [...p.tables, ...p.references];
    const defs = all.map(t => tableNode(t, p.group, p.edges.filter(e => e[2] === t).map(e => e[3]), p.references.includes(t)));
    const links = p.edges.map(([from, fromColumn, to, toColumn, count]) => {
      const color = from === 'students' ? '#357052' : from === 'events' ? '#A74455' : from === 'attendance_devices' ? '#3B718E' : '#687078';
      return `${from}:${vertical ? 's' : 'e'} -> ${to}:${vertical ? 'n' : 'w'} [dir=both, arrowtail="teeodot", arrowhead="${count === 'one' ? 'teeodot' : 'crowodot'}", color="${color}", style=dashed];`;
    });
    const ranks = vertical ? `{ rank=same; ${p.references.join('; ')}; }` : '';
    const dot = `digraph ERD { graph [rankdir=${vertical ? 'TB' : 'LR'}, bgcolor="white", pad=0.35, nodesep=0.5, ranksep=0.9, splines=polyline, concentrate=false, outputorder=edgesfirst]; node [shape=plain, fontname="Arial", fontsize=14]; edge [arrowsize=0.8, penwidth=1.2]; ${defs.join('\n')} ${ranks} ${links.join('\n')} }`;
    fs.writeFileSync(path.join(__dirname, p.id + '.dot'), dot);
    const svg = viz.renderString(dot, { format: 'svg' });
    fs.writeFileSync(path.join(__dirname, p.id + '.svg'), svg);
    const png = path.join(__dirname, p.id + '.png');
    await sharp(Buffer.from(svg), { density: 180, limitInputPixels: false }).png().toFile(png);
    const metadata = await sharp(png).metadata();
    panelFiles.push({ ...p, png, width: metadata.width, height: metadata.height });
    console.log(`${p.id}: ${p.tables.length} tables, ${metadata.width}x${metadata.height}`);
  }
  const columnWidth = 2700, margin = 90, titleHeight = 190, panelHeading = 95, gap = 85;
  const placements = [], rowHeights = [];
  for (let row = 0; row < 3; row++) {
    const items = panelFiles.slice(row * 2, row * 2 + 2);
    const heights = items.map(p => Math.round(p.height * columnWidth / p.width));
    rowHeights.push(Math.max(...heights, row === 2 ? 1200 : 0) + panelHeading);
  }
  const width = columnWidth * 2 + margin * 2 + gap;
  const height = titleHeight + rowHeights.reduce((a, b) => a + b, 0) + gap * 2 + margin;
  let y = titleHeight;
  const text = [];
  for (let row = 0; row < 3; row++) {
    for (let col = 0; col < 2; col++) {
      const p = panelFiles[row * 2 + col];
      const x = margin + col * (columnWidth + gap);
      if (!p) continue;
      const buffer = await sharp(p.png).resize({ width: columnWidth }).png().toBuffer();
      placements.push({ input: buffer, left: x, top: y + panelHeading });
      text.push(`<text x="${x}" y="${y + 55}" font-size="39" font-weight="bold">${escape(p.title)}</text>`);
    }
    y += rowHeights[row] + (row < 2 ? gap : 0);
  }
  const legendX = margin + columnWidth + gap, legendY = titleHeight + rowHeights[0] + rowHeights[1] + gap * 2;
  const legend = [
    'LEGEND AND SCHEMA NOTES',
    'PK  Primary key       UQ  Unique key       AI  Auto-increment',
    'NN  NOT NULL          LR  Logical reference',
    'NULL  Nullable column; column defaults omitted for readability.',
    'Dashed connectors show logical associations used by the application.',
    'Circle + bar: zero or one. Circle + crow foot: zero or many.',
    'The current schema does not declare SQL foreign-key constraints.',
    'Reference-only boxes repeat keys from tables shown in another panel.',
    'There are 23 distinct tables; reference boxes are not additional tables.',
    'Historical records may lack a matching current parent record.',
    'Recorded device names and audit actors are stored text snapshots.',
    'EVENT_COMPLETION_SCANS.event_log_id is not a unique key.',
    'A source event log may be removed while evidence is retained.',
    'Campus visits are distinct from event participation records.',
    'Figure 16. Entity-Relationship Diagram',
    'Source: current NFC_System schema creation and migration code.'
  ];
  legend.forEach((line, i) => text.push(`<text x="${legendX + 40}" y="${legendY + 65 + i * 65}" font-size="${i === 0 ? 40 : 32}" ${i === 0 ? 'font-weight="bold"' : ''}>${escape(line)}</text>`));
  const overlay = `<svg width="${width}" height="${height}" xmlns="http://www.w3.org/2000/svg"><g font-family="Arial" fill="#1F2933"><text x="${margin}" y="78" font-size="59" font-weight="bold">NFC_SYSTEM - ENTITY-RELATIONSHIP DIAGRAM</text><text x="${margin}" y="137" font-size="33">23 tables | Core records, attendance, event completion, synchronization and security audits</text>${text.join('')}</g></svg>`;
  placements.push({ input: Buffer.from(overlay), left: 0, top: 0 });
  const output = path.join(__dirname, 'NFC-System-Updated-ERD.png');
  await sharp({ create: { width, height, channels: 3, background: 'white' }, limitInputPixels: false }).composite(placements).png().toFile(output);
  await sharp(output, { limitInputPixels: false }).resize({ width: 1400 }).png().toFile(path.join(__dirname, 'ERD-preview.png'));
  console.log(`Complete ERD: ${width}x${height}; ${output}`);
}
main().catch(e => { console.error(e); process.exitCode = 1; });
