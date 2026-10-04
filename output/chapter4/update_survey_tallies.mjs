import fs from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';

const input = 'C:/Users/justine mendoza/Documents/METRICS first batch/UnStudent-Weighted-Mean.xlsx';
const wb = await SpreadsheetFile.importXlsx(await FileBlob.load(input));
const sheet = wb.worksheets.getItem('Students');
const previewOnly = process.argv.includes('--preview');
if (previewOnly) {
  console.log((await wb.inspect({kind:'sheet',include:'id,name',maxChars:2000})).ndjson);
  for (const [range,name] of [['A3:J10','system'],['A166:J173','manual']]) {
    const image = await wb.render({sheetName:'Students',range,scale:1.2,format:'png'});
    await fs.writeFile(new URL(`./Tally-Before-${name}.png`,import.meta.url),new Uint8Array(await image.arrayBuffer()));
  }
  process.exit(0);
}

const responses = await SpreadsheetFile.importXlsx(await FileBlob.load(fileURLToPath(new URL('./Survey-Response-Entry-Template.xlsx',import.meta.url))));
const groups = [
  ['System', [[5,9],[13,16],[20,24],[28,32],[36,40]], [10,17,25,33,41],43],
  ['Manual', [[168,172],[176,179],[183,187],[191,195],[199,203]], [173,180,188,196,204],206],
];
const manifest = {input,source:fileURLToPath(new URL('./Survey-Response-Entry-Template.xlsx',import.meta.url)),inputCells:[],dependentCells:[],changedCounts:0};
for (const [name,blocks,categoryRows,overall] of groups) {
  const rows = responses.worksheets.getItem(name).getRange('B2:Y71').values;
  if (rows.length!==70 || rows.some(row=>row.length!==24 || row.some(v=>typeof v!=='number'||![1,2,3,4].includes(v)))) throw Error(`${name}: invalid responses`);
  let index=0;
  for (const [start,end] of blocks) {
    const counts=[];
    for (let r=start;r<=end;r++,index++) {
      const row=[4,3,2,1].map(value=>rows.filter(values=>values[index]===value).length);
      const old=sheet.getRange(`B${r}:E${r}`).values[0];
      row.forEach((value,c)=>{if(value!==old[c])manifest.changedCounts++;manifest.inputCells.push(`${'BCDE'[c]}${r}`);});
      counts.push(row);
      for (const c of 'FGHIJ') manifest.dependentCells.push(`${c}${r}`);
    }
    sheet.getRange(`B${start}:E${end}`).values=counts;
  }
  for (const r of [...categoryRows,overall]) for (const c of 'HIJ') manifest.dependentCells.push(`${c}${r}`);
}
// Refresh imported formula caches without changing the existing formulas.
const originalFormulas = JSON.parse(await fs.readFile(new URL('./tally-original-formulas.json',import.meta.url),'utf8'));
const affectedFormulas = manifest.dependentCells.map(address=>[address,originalFormulas[address]]);
for (const [address,formula] of affectedFormulas) {
  if (!formula?.startsWith('=')) throw Error(`Expected formula at ${address}`);
  sheet.getRange(address).clear({applyTo:'contents'});
}
for (const [address,formula] of affectedFormulas) sheet.getRange(address).formulas=[[formula]];
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Students!B5:J9',include:'values,formulas',tableMaxRows:5,tableMaxCols:9,maxChars:2500})).ndjson);
console.log('System overall',sheet.getRange('H43').values,'Manual overall',sheet.getRange('H206').values);
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:30}})).ndjson);
for (const [range,name] of [['A3:J10','system'],['A166:J173','manual'],['H198:J206','manual-summary']]) {
  const image=await wb.render({sheetName:'Students',range,scale:1.2,format:'png'});
  await fs.writeFile(new URL(`./Tally-After-${name}.png`,import.meta.url),new Uint8Array(await image.arrayBuffer()));
}
await fs.writeFile(new URL('./tally-update-manifest.json',import.meta.url),JSON.stringify(manifest,null,2));
const file=await SpreadsheetFile.exportXlsx(wb);
await file.save(fileURLToPath(new URL('./tally-update-working.xlsx',import.meta.url)));
console.log(`Updated 48 question tallies; ${manifest.changedCounts} individual count cells changed.`);
