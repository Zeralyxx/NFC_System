import hashlib
import json
import re
import statistics
from pathlib import Path

import pdfplumber
from pypdf import PdfReader

ROOT = Path(__file__).parent
THESIS = Path(r'C:\Users\justine mendoza\Videos\Final Draft Chapter 1-5 Thesis.pdf')
MANUAL = Path(r'C:\Users\justine mendoza\Documents\METRICS first batch\Manual Process Baseline Testing - Sheet1.pdf')
manual_reader = PdfReader(MANUAL)
manual_text = '\n'.join(p.extract_text() for p in manual_reader.pages)
observations = [dict(respondent=int(i), seconds=float(value))
                for i, value in re.findall(r'Respondent\s+(\d+)\s+(\d+\.\d+)', manual_text)]
assert [r['respondent'] for r in observations] == list(range(1, 71))
times = [r['seconds'] for r in observations]
manual = dict(source=str(MANUAL), sha256=hashlib.sha256(MANUAL.read_bytes()).hexdigest(),
              observations=observations, n=len(times), sum_s=sum(times), mean_s=statistics.mean(times),
              median_s=statistics.median(times), sample_sd_s=statistics.stdev(times),
              min_s=min(times), max_s=max(times), service_equivalent_per_min=60/statistics.mean(times),
              timing_boundary='Awaiting researcher clarification', observation_date='Awaiting researcher clarification',
              enrollment_check='Awaiting researcher clarification', same_participants='Awaiting researcher clarification')
(ROOT / 'manual.json').write_text(json.dumps(manual, indent=2), encoding='utf-8')

def clean(value):
    return ' '.join(str(value or '').split())

labels = [('Students', name, 70) for name in ['Functional Suitability', 'Performance Efficiency', 'Usability', 'Reliability', 'Security']]
labels += [('Security Personnel', name, 5) for name in ['Functional Suitability', 'Performance Efficiency', 'Usability', 'Reliability', 'Security']]
labels += [('Faculty Event Organizers Administrators', name, 5) for name in ['Functional Suitability', 'Performance Efficiency', 'Compatibility', 'Usability', 'Reliability', 'Security', 'Maintainability', 'Portability']]
surveys, current, pages = [], [], []
with pdfplumber.open(THESIS) as pdf:
    demographics = [[clean(c) for c in row] for row in pdf.pages[122].extract_tables()[0]]
    for i in range(124, 141):
        for table in pdf.pages[i].extract_tables():
            for row in table:
                row = [clean(c) for c in row]
                if not row or len(row) != 4 or row[0] == 'Question':
                    continue
                if not any(row[1:]):
                    assert current, (i + 1, row)
                    current[-1][0] += ' ' + row[0]
                    continue
                current.append(row)
                pages.append(i + 1)
                if row[0].startswith('Overall Weighted Mean'):
                    group, characteristic, n = labels[len(surveys)]
                    items = [dict(question=r[0], mean=float(r[1]), sd=float(r[2]), interpretation=r[3]) for r in current[:-1]]
                    overall = current[-1]
                    surveys.append(dict(group=group, characteristic=characteristic, n=n,
                        pages=sorted(set(pages)), items=items, reported_mean=float(overall[1]),
                        reported_sd=float(overall[2]), interpretation=overall[3]))
                    current, pages = [], []
assert len(surveys) == 18 and not current
survey_audit = []
for table in surveys:
    mean_of_printed_items = statistics.mean(r['mean'] for r in table['items'])
    if abs(mean_of_printed_items - table['reported_mean']) > 0.015:
        survey_audit.append(dict(group=table['group'], characteristic=table['characteristic'],
            issue='Characteristic mean does not match the mean of printed item means',
            reported=table['reported_mean'], calculated=mean_of_printed_items))
    for row in table['items']:
        if row['sd'] == 0 and row['mean'] != round(row['mean']):
            survey_audit.append(dict(group=table['group'], characteristic=table['characteristic'],
                question=row['question'], issue='Zero SD incompatible with fractional mean of integer 1-4 responses'))
survey_data = dict(source=str(THESIS), tables=surveys, demographics=demographics, audit=survey_audit)
(ROOT / 'survey-extract.json').write_text(json.dumps(survey_data, indent=2), encoding='utf-8')

reader = PdfReader(THESIS)
assets = ROOT / 'full-chapter-assets'
assets.mkdir(exist_ok=True)
figure = 1
for page_index in range(104, 110):
    for item in reader.pages[page_index].images:
        bitmap = item.image
        if bitmap.width < 1000 or bitmap.height < 200:
            continue
        bitmap.save(assets / f'Interface-{figure}.png')
        figure += 1
assert figure == 7, figure
print(json.dumps({k: v for k, v in manual.items() if k != 'observations'}, indent=2))
print('Survey tables:', len(surveys), 'Interface figures:', figure - 1)
print('Survey audit:', json.dumps(survey_audit, indent=2))
