import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { FileBlob, PresentationFile } from '@oai/artifact-tool';

const build = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(build, 'source');
await fs.mkdir(out, { recursive: true });
const p = await PresentationFile.importPptx(await FileBlob.load('C:/Users/justine mendoza/Documents/Pre-oral_IMRAD.pptx'));
await fs.writeFile(path.join(out, 'source-proto.json'), JSON.stringify(p.toProto(), null, 2));
const inspect = await p.inspect({ kind: 'slide,textbox,shape,image,table,chart,layout', maxChars: 200000 });
await fs.writeFile(path.join(out, 'inspect.ndjson'), inspect.ndjson);
console.log('Slides:', p.slides.items.length);
console.log('Masters:', p.masters.items.length);
for (const [i, slide] of p.slides.items.entries()) {
  const png = await p.export({ slide, format: 'png', scale: 1.5 });
  await fs.writeFile(path.join(out, `slide-${String(i + 1).padStart(2, '0')}.png`), new Uint8Array(await png.arrayBuffer()));
  console.log('Rendered source', i + 1);
}
