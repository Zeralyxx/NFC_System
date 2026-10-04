const fs = require('fs');
const path = require('path');
const runtime = 'C:/Users/justine mendoza/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
const { instance } = require(path.join(runtime, '@viz-js/viz'));
const sharp = require(path.join(runtime, 'sharp'));
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
function box(title, lines, color, shape = 'box') {
  return `[shape=${shape}, label=<<TABLE BORDER="0" CELLBORDER="0" CELLPADDING="5"><TR><TD><B>${esc(title)}</B></TD></TR>${lines.map(l => `<TR><TD>${esc(l)}</TD></TR>`).join('')}</TABLE>>, fillcolor="${color}"];`;
}
const dot = `digraph architecture {
 graph [rankdir=TB, bgcolor="white", fontname="Arial", fontsize=23, labelloc=t,
 label="Figure 17 - System Architecture\nNFC, PIN and Signed QR Verification with Local Persistence and Cloud Synchronization",
 pad=0.45, nodesep=0.55, ranksep=0.85, splines=polyline, compound=true, outputorder=edgesfirst];
 node [fontname="Arial", fontsize=16, style="filled", color="#83909C", penwidth=1.2, margin="0.12,0.10"];
 edge [fontname="Arial", fontsize=12, color="#596975", arrowsize=0.8, penwidth=1.3];

 subgraph cluster_inputs {
  label="USERS AND CONNECTED HARDWARE"; color="#BAC7C0"; bgcolor="#F4F8F5"; fontsize=19;
  student ${box('Student', ['NFC card, PIN and printed QR'], '#DDECE2')}
  personnel ${box('Authorized Personnel', ['Administrator / Master Admin', 'Security Personnel / Event Organizer'], '#DDECE2')}
  terminal ${box('ESP32 + PN532 Reader', ['NFC UID acquisition', 'Physical keypad input, when connected', 'Serial LED command support'], '#E9F2EC')}
  webcam ${box('PC Camera', ['Integrated camera or USB webcam', 'Video frames captured by Windows'], '#E9F2EC')}
  student -> terminal [label="Tap card / enter PIN"];
  student -> webcam [label="Present QR"];
  { rank=same; terminal; webcam; personnel; }
 }

 subgraph cluster_pc {
  label="WINDOWS PC - C# / WinUI 3 DESKTOP APPLICATION\nVerification logic and kiosk windows run on this host";
  color="#7295AA"; bgcolor="#F4F8FC"; fontsize=20;
  hardware ${box('Serial Hardware Service', ['COM port at 115200 baud', 'UID= and KEY= input messages', 'Reader status / reconnection'], '#DCEBF5')}
  camera ${box('Camera and QR Decoder', ['MediaCapture / MediaFrameReader', 'ZXing QR decoding', 'Preview and scan throttling'], '#DCEBF5')}
  ui ${box('Operator Interface and Session Control', ['Staff sign-in and role checks', 'Gate direction / configured mode', 'Registration and event selection'], '#DCEBF5')}
  kiosk ${box('Student Kiosk and Security Interface', ['Student profile, prompts and results', 'PIN entry / QR preview / attendance times', 'Security alerts and monitoring', 'Same PC; primary or extended display'], '#DCEBF5')}
  { rank=same; hardware; camera; ui; kiosk; }

  verification ${box('Verification and Credential Services', ['Student status and restricted-event checks', 'Fast: NFC | Standard: NFC + PIN', 'High Security: NFC + PIN + signed QR', 'QR fallback with PIN when selected', 'PIN lockout / entry-exit sequence checks'], '#E5E3F2')}
  operations ${box('Administration and Reporting Services', ['Student directory / CSV-XLSX import', 'Event creation and roster management', 'Duration-based completion and review', 'Graduation clearance / analytics / exports'], '#E5E3F2')}
  attendance ${box('Attendance and Security Persistence', ['Campus visits / event scan evidence', 'Device identity and transaction sequences', 'Receipts / repeat-safe processing', 'PIN-state reconciliation / audit logging'], '#E5E3F2')}
  { rank=same; verification; operations; attendance; }

  cache ${box('Persistent Local Cache and Journals', ['Cached students, events and rosters', 'Durable attendance outbox and PIN journal', 'Files on the Windows PC; RAM working cache', 'Database outage fallback / ordered replay'], '#F7ECCF')}
  trust ${box('QR Signing and Trust Material', ['Enrollment private key: Windows certificate store', 'Trusted public certificates: local configuration', 'ECDSA P-256 signing / signature verification'], '#F7ECCF')}
  synchronization ${box('Database Monitoring and Synchronization', ['MySQL connectivity checks / queue recovery', 'Firestore collection push and pull', 'Roster conflict handling', 'Attendance snapshot backup / restore'], '#F7ECCF')}
  { rank=same; cache; trust; synchronization; }

  hardware -> verification [label="UID / keypad events"];
  camera -> verification [label="Decoded QR payload"];
  ui -> verification [label="Configured transaction"];
  ui -> operations [label="Authorized actions"];
  verification -> kiosk [label="Prompts / result / alerts", constraint=false];
  kiosk -> verification [label="PIN / QR input", constraint=false];
  verification -> attendance [label="Outcome / evidence"];
  operations -> attendance [label="Reviews / audit changes", constraint=false];
  trust -> verification [constraint=false];
  attendance -> cache [label="Durable pending records", dir=both];
  verification -> cache [label="Fallback lookup", style=dashed];
  synchronization -> cache [dir=both, constraint=false];
 }

 mysql ${box('MySQL Database - 23 Tables', ['Student, staff and event records', 'Logs, alerts, visits and device metadata', 'PIN security, roster and completion evidence', 'Graduation records and audit history', 'Same PC or configured LAN database host'], '#DDE9E2', 'cylinder')}
 cloud ${box('Firebase Firestore', ['Selected profile / event / log synchronization', 'Roster synchronization documents', 'Compressed attendance backup snapshots', 'Internet-dependent cloud storage'], '#F5E1DF', 'cylinder')}
 browser ${box('Web PIN Recovery', ['Student browser / Firebase Hosting files', 'Firestore JavaScript SDK', 'PIN hash and salt updated in cloud', 'Local changes require subsequent pull'], '#F5E1DF')}
 email ${box('EmailJS Email Service', ['Verification-code email delivery', 'Used by the web recovery page'], '#F5E1DF')}
 other ${box('Additional Verification PCs', ['Same desktop application and hardware pattern', 'Independent device ID / persistent local queues', 'Configured to the shared LAN MySQL host'], '#EDF0F2')}
 note ${box('Architecture Notes', ['Solid arrows: data or control paths', 'Dashed arrows: fallback or optional deployment paths', 'USB serial is distinct from LAN communication', 'Mode selection follows configured policy', 'Cloud synchronization is separate from local queue replay'], '#FFFFFF')}

 terminal -> hardware [label="USB serial / COM", dir=both, color="#367555", penwidth=2];
 webcam -> camera [label="Windows camera API", color="#367555", penwidth=2];
 personnel -> ui [label="Operator actions"];
 attendance -> mysql [label="Async SQL / transactions", dir=both, color="#3B708F", penwidth=2];
 operations -> mysql [label="Async queries / updates", dir=both, color="#3B708F"];
 synchronization -> mysql [label="Refresh / replay / snapshot", dir=both, color="#3B708F"];
 synchronization -> cloud [label="HTTPS REST / push-pull", dir=both, color="#A3535A", penwidth=2];
 browser -> cloud [label="HTTPS / read-update", dir=both, color="#A3535A"];
 browser -> email [label="Send verification code", color="#A3535A"];
 other -> mysql [label="LAN / MySQL protocol", dir=both, style=dashed, color="#3B708F"];
 mysql -> note [style=invis];
 { rank=same; mysql; cloud; browser; other; }
 { rank=same; email; note; }
}`;

async function main() {
  const viz = await instance();
  const svg = viz.renderString(dot, { format: 'svg' });
  const boxes = [...svg.matchAll(/<g id="node\d+" class="node">([\s\S]*?)<\/g>/g)];
  if (boxes.length !== 20 || boxes.some(b => !b[1].includes('<text'))) throw new Error('Architecture node rendering validation failed.');
  fs.writeFileSync(path.join(__dirname, 'NFC-System-Architecture.dot'), dot);
  fs.writeFileSync(path.join(__dirname, 'NFC-System-Architecture.svg'), svg);
  const png = path.join(__dirname, 'NFC-System-Architecture.png');
  await sharp(Buffer.from(svg), { density: 150, limitInputPixels: false }).png().toFile(png);
  await sharp(png, { limitInputPixels: false }).resize({ width: 1600 }).png().toFile(path.join(__dirname, 'Architecture-preview.png'));
  console.log(JSON.stringify({ nodes: boxes.length, png, dimensions: await sharp(png).metadata() }));
}
main().catch(e => { console.error(e); process.exitCode = 1; });
