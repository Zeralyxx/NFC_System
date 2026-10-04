import fs from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const source = JSON.parse(await fs.readFile(new URL('./survey-analysis.json', import.meta.url), 'utf8'));
const wb = Workbook.create();
const summary = wb.worksheets.add('Summary');
const sheets = [wb.worksheets.add('Manual'), wb.worksheets.add('System')];
const categories = [
  ['FS', 'Functional Suitability', 'B', 'F', 'Z', 5],
  ['PE', 'Performance Efficiency', 'G', 'J', 'AA', 4],
  ['US', 'Usability', 'K', 'O', 'AB', 5],
  ['RE', 'Reliability', 'P', 'T', 'AC', 5],
  ['SE', 'Security', 'U', 'Y', 'AD', 5],
];
const colors = {ink:'#263638', header:'#285D62', pale:'#EAF1F2', input:'#FFF3CD'};
function base(sheet, range) {
  sheet.showGridLines = false;
  const r = sheet.getRange(range);
  r.format.font = {name:'Arial', size:10, color:colors.ink};
  r.format.rowHeight = 23;
  r.format.columnWidth = 12;
  r.format.verticalAlignment = 'center';
}
function header(sheet, range) {
  const r = sheet.getRange(range);
  r.format.fill = colors.header;
  r.format.font = {name:'Arial', size:10, bold:true, color:'#FFFFFF'};
  r.format.wrapText = true;
  r.format.rowHeight = 43;
}
function note(sheet, range, text, height = 30) {
  const r = sheet.getRange(range);
  r.merge(); r.values = [[text]];
  r.format.wrapText = true; r.format.rowHeight = height;
}
for (let index = 0; index < sheets.length; index++) {
  const sheet = sheets[index];
  const name = index === 0 ? 'Manual' : 'System';
  const group = source.groups.find(g => g.name === (index === 0 ? 'Manual Pre-Evaluation' : 'Students'));
  base(sheet, 'A1:AG105');
  const questionCodes = categories.flatMap(([code,,,,,count]) => Array.from({length:count}, (_,i) => `${code}${i+1}`));
  sheet.getRange('A1:AG1').values = [['Paper ID', ...questionCodes, ...categories.map(c => c[1]), 'Overall score', 'Entry status', 'Checked by']];
  header(sheet, 'A1:AG1');
  sheet.getRange('A:A').format.columnWidth = 13;
  sheet.getRange('B:Y').format.columnWidth = 7;
  sheet.getRange('Z:AE').format.columnWidth = 18;
  sheet.getRange('AF:AF').format.columnWidth = 20;
  sheet.getRange('AG:AG').format.columnWidth = 15;
  sheet.getRange('A2:A71').values = Array.from({length:70}, (_,i) => [`${index === 0 ? 'M' : 'S'}${String(i+1).padStart(2,'0')}`]);
  sheet.getRange('B2:Y71').format.fill = colors.input;
  sheet.getRange('AG2:AG71').format.fill = colors.input;
  sheet.getRange('B2:AE71').format.horizontalAlignment = 'center';
  sheet.getRange('B2:Y71').setNumberFormat('0');
  sheet.getRange('Z2:AE71').setNumberFormat('0.000');
  sheet.dataValidations.add({range:'B2:Y71',rule:{type:'whole',operator:'between',formula1:1,formula2:4}});
  sheet.getRange('AF2:AF71').formulas = Array.from({length:70}, (_,i) => {
    const r=i+2, valid = [1,2,3,4].map(n => `COUNTIF(B${r}:Y${r},${n})`).join('+');
    return [`=IF(COUNTA(B${r}:Y${r})=0,"Not entered",IF(AND(COUNT(B${r}:Y${r})=24,${valid}=24),"Complete","CHECK PAPER"))`];
  });
  sheet.getRange('Z2:AE71').formulas = Array.from({length:70}, (_,i) => {
    const r=i+2;
    return [...categories.map(c => `=IF($AF${r}="Complete",AVERAGE(${c[2]}${r}:${c[3]}${r}),"")`), `=IF($AF${r}="Complete",AVERAGE(Z${r}:AD${r}),"")`];
  });
  sheet.freezePanes.freezeRows(1);
  note(sheet, 'A74:K74', `${name}: enter one paper per row. Yellow answer cells accept whole numbers 1-4 only. Do not enter names or student IDs.`, 35);
  note(sheet, 'A75:K75', 'Use the question key below to match section and item numbers. Leave missing or unclear answers blank; do not guess.', 35);
  header(sheet, 'A77:K77');
  sheet.getRange('A77').values = [['Item code']];
  note(sheet, 'B77:K77', 'Question wording in the supplied tally', 43);
  let itemIndex = 0;
  for (const [code,category,,,,count] of categories) {
    const items = group.items.filter(item => item.category === category);
    for (let j=0;j<count;j++) {
      const r=78+itemIndex++;
      sheet.getRange(`A${r}`).values = [[`${code}${j+1}`]];
      note(sheet, `B${r}:K${r}`, items[j].question, 44);
    }
  }
  note(sheet, 'A103:K103', `Question key source: ${source.source}`, 38);
}
base(summary, 'A1:H36');
summary.getRange('A:A').format.columnWidth = 29;
summary.getRange('B:H').format.columnWidth = 16;
note(summary, 'A1:H1', 'Manual and Proposed System | Survey Entry', 32);
summary.getRange('A1:H1').format.font = {name:'Arial',size:14,bold:true,color:colors.ink};
note(summary, 'A3:H3', 'Purpose: preserve each respondent\'s answers for an independent-samples comparison. Manual and system papers are separate groups.', 35);
const steps = [
  '1. Count the papers. This template has 70 slots for each group; report any different actual count.',
  '2. Label manual papers M01-M70 and system student papers S01-S70. No matching between groups is needed.',
  '3. Enter each paper in its matching row on Manual or System. Use its original 1-4 response codes; enter only yellow cells.',
  '4. Check the section and question number using the key below each entry sheet. Do not replace an individual answer with an average.',
  '5. Have a second teammate check every entry against the paper, then enter their initials in Checked by.',
  '6. Resolve CHECK PAPER rows using the original paper. Keep genuinely missing answers blank and report them.',
];
steps.forEach((text,i) => note(summary, `A${5+i}:H${5+i}`, text, 35));
summary.getRange('A12:D12').values = [['Data readiness','Manual','System','Expected per group']];
header(summary, 'A12:D12');
summary.getRange('A13:A15').values = [['Complete forms'],['Rows needing a check'],['Not entered']];
for (const [col,name] of [['B','Manual'],['C','System']]) {
  summary.getRange(`${col}13:${col}15`).formulas = ['Complete','CHECK PAPER','Not entered'].map(status => [`=COUNTIF(${name}!AF2:AF71,"${status}")`]);
}
summary.getRange('D13:D15').values = [[70],[0],[0]];
note(summary, 'A17:H17', 'The scores below appear only when all 70 forms in that group have valid answers. They are descriptive results, not the t-test.', 35);
summary.getRange('A19:E19').values = [['Score','Manual mean','Manual sample SD','System mean','System sample SD']];
header(summary, 'A19:E19');
const scoreRows = [...categories.map(c => [c[1],c[4]]), ['Overall score','AE']];
scoreRows.forEach(([label,col],i) => {
  const r=20+i;
  summary.getRange(`A${r}`).values = [[label]];
  summary.getRange(`B${r}:E${r}`).formulas = [[
    `=IF($B$13=70,AVERAGE(Manual!${col}2:${col}71),"")`,
    `=IF($B$13=70,STDEV(Manual!${col}2:${col}71),"")`,
    `=IF($C$13=70,AVERAGE(System!${col}2:${col}71),"")`,
    `=IF($C$13=70,STDEV(System!${col}2:${col}71),"")`,
  ]];
});
summary.getRange('B20:E25').setNumberFormat('0.000');
note(summary, 'A27:H27', 'Scoring: average each category\'s answers, then average the five category scores equally. Performance Efficiency has 4 items; each other category has 5.', 40);
note(summary, 'A29:H29', 'Next analysis: a two-sided Welch independent-samples t-test on each person\'s overall score, after checking data entry, scoring direction, and comparable survey content.', 40);
note(summary, 'A31:H31', 'Do not combine security-personnel or administrator forms with the student comparison. Keep those forms separate.', 35);
note(summary, 'A33:H33', 'This file does not test processing-time differences. Manual writing times and automated component timers do not measure the same complete task.', 35);
note(summary, 'A35:H35', `Source for questionnaire order and wording: ${source.source}`, 38);
summary.freezePanes.freezeRows(1);

// Exercise incomplete, out-of-range, fractional, and complete rows without retaining test answers.
const testSheet = sheets[0];
for (const [label,values,expected,score] of [
  ['empty',Array(24).fill(null),'Not entered',null],
  ['incomplete',[1,...Array(23).fill(null)],'CHECK PAPER',null],
  ['out of range',[5,...Array(23).fill(4)],'CHECK PAPER',null],
  ['fractional',[2.5,...Array(23).fill(4)],'CHECK PAPER',null],
  ['complete',categories.flatMap((c,i)=>Array(c[5]).fill(i===4?1:i+1)),'Complete',2.2],
]) {
  testSheet.getRange('B2:Y2').values = [values];
  wb.recalculate();
  const actual = testSheet.getRange('AF2').values[0][0];
  const actualScore = testSheet.getRange('AE2').values[0][0];
  if (actual !== expected || (score !== null && Math.abs(actualScore-score)>1e-10)) throw new Error(`${label}: ${actual}, score ${actualScore}`);
}
testSheet.getRange('B2:Y71').values = Array.from({length:70},(_,i)=>Array(24).fill(i%2===0?1:4));
wb.recalculate();
if (summary.getRange('B13').values[0][0] !== 70 || summary.getRange('B25').values[0][0] !== 2.5 || Math.abs(summary.getRange('C25').values[0][0]-Math.sqrt(70*2.25/69))>1e-10) {
  throw new Error('Complete-group mean / sample-SD check failed.');
}
testSheet.getRange('B2:Y71').clear({applyTo:'contents'});
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Summary!A12:D15',include:'values,formulas',tableMaxRows:4,tableMaxCols:4})).ndjson);
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:30}})).ndjson);
for (const [sheetName,range,suffix] of [
  ['Summary','A1:H35','summary'], ['Manual','A1:J12','manual-input'],
  ['System','Z1:AG12','system-scores'], ['Manual','A77:K82','question-key'],
]) {
  const render = await wb.render({sheetName,range,scale:1.4,format:'png'});
  await fs.writeFile(new URL(`./Survey-Entry-${suffix}.png`,import.meta.url),new Uint8Array(await render.arrayBuffer()));
}
const output = await SpreadsheetFile.exportXlsx(wb);
await output.save(fileURLToPath(new URL('./Survey-Response-Entry-Template.xlsx',import.meta.url)));
console.log('Survey template exported. All raw answer cells restored to blank.');
