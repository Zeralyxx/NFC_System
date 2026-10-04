import hashlib
import json
import math
import re
from pathlib import Path

import pypdfium2 as pdfium
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader

ROOT = Path(__file__).parent
SOURCE = Path('C:/Users/justine mendoza/Documents/METRICS first batch/UnStudent Weighted Mean - Students (1).pdf')
reader = PdfReader(SOURCE)
assert len(reader.pages) == 27
left = '\n'.join(p.extract_text() for p in reader.pages[:9])
middle = '\n'.join(p.extract_text() for p in reader.pages[9:18])
questions = list(re.finditer(r'(?<!\d)([1-5])\.\s+(.*?)(\d+)\s+(\d+)\s*(?=\n|$)', left, re.S))
numeric = list(re.finditer(r'(?m)^[ \t]*(\d+)[ \t]+(\d+)[ \t]+(\d+)[ \t]+(\d+)[ \t]+([\d.]+)(?:[ \t]+([\d.]+))?[ \t]*$', middle))
assert len(questions) == len(numeric) == 111, (len(questions), len(numeric))

layout = [
    ('Students', 70, [('Functional Suitability', 5), ('Performance Efficiency', 4), ('Usability', 5), ('Reliability', 5), ('Security', 5)]),
    ('Security Personnel', 5, [('Functional Suitability', 5), ('Performance Efficiency', 4), ('Usability', 5), ('Reliability', 5), ('Security', 5)]),
    ('Faculty / Event Organizer / Admin', 5, [('Functional Suitability', 5), ('Performance Efficiency', 5), ('Compatibility', 4), ('Usability', 5), ('Reliability', 5), ('Security', 5), ('Maintainability', 5), ('Portability', 5)]),
    ('Manual Pre-Evaluation', 70, [('Functional Suitability', 5), ('Performance Efficiency', 4), ('Usability', 5), ('Reliability', 5), ('Security', 5)]),
]

def interpretation(mean):
    if mean >= 3.26:
        return 'Strongly Agree / Highly Satisfactory'
    if mean >= 2.51:
        return 'Agree / Satisfactory'
    if mean >= 1.76:
        return 'Disagree / Unsatisfactory'
    return 'Strongly Disagree / Highly Unsatisfactory'

groups = []
index = 0
for group_name, expected_n, categories in layout:
    group = {'name': group_name, 'respondents': expected_n, 'categories': [], 'items': []}
    for category_name, count in categories:
        items = []
        for number in range(1, count + 1):
            q, numeric_row = questions[index], numeric[index]
            assert int(q[1]) == number, (index, q[1], number)
            f4, f3 = int(q[3]), int(q[4])
            f2, f1, n, score = map(int, numeric_row.groups()[:4])
            mean_printed = float(numeric_row[5])
            assert f4 + f3 + f2 + f1 == n == expected_n, (group_name, category_name, number, [f4, f3, f2, f1], n)
            total = 4*f4 + 3*f3 + 2*f2 + f1
            assert total == score, (index, total, score)
            mean = total/n
            assert abs(mean - mean_printed) <= 0.00501, (index, mean, mean_printed)
            squared = sum(f*(rating-mean)**2 for rating, f in zip([4,3,2,1], [f4,f3,f2,f1]))
            item = {'category': category_name, 'number': number, 'question': ' '.join(q[2].split()),
                    'frequencies': [f4,f3,f2,f1], 'n': n, 'score': total, 'mean': mean,
                    'sample_sd': math.sqrt(squared/(n-1)), 'population_sd': math.sqrt(squared/n),
                    'interpretation': interpretation(mean)}
            items.append(item)
            group['items'].append(item)
            index += 1
        mean = sum(x['mean'] for x in items)/len(items)
        group['categories'].append({'name': category_name, 'items': count, 'mean': mean,
                                    'item_sd_min': min(x['population_sd'] for x in items),
                                    'item_sd_max': max(x['population_sd'] for x in items),
                                    'interpretation': interpretation(mean)})
    group['overall_category_mean'] = sum(x['mean'] for x in group['categories'])/len(group['categories'])
    group['overall_item_mean'] = sum(x['mean'] for x in group['items'])/len(group['items'])
    group['interpretation'] = interpretation(group['overall_category_mean'])
    groups.append(group)

result = {'source': SOURCE.as_posix(), 'sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(), 'groups': groups,
          'notes': ['Category means are calculated from unrounded item means.',
                    'Overall group means give equal weight to categories, consistent with the worksheet description.',
                    'The administrative worksheet displays overall 3.8. The unrounded equal-category mean is 3.81375, displayed as 3.81 when reporting two decimal places.',
                    'Student usability category recomputes to 3.754285714 (3.75); averaging the printed rounded item means yields 3.76.',
                    'Chapter 3 uses N in the SD denominator. The chapter uses population-form item SD; the audit also preserves sample-form SD for comparison with the worksheet.',
                    'Worksheet category SD values appear to average item sample SDs; such an average is not the SD of respondents\' category scores.',
                    'Item-frequency tables do not identify respondent-level cross-item scores; composite-score SD cannot be recovered.',
                    'No manual/system participant linkage or pre-evaluation timing is assumed.']}
(ROOT/'survey-analysis.json').write_text(json.dumps(result, indent=2), encoding='utf-8')

lines = ['# Survey Calculation Audit', '', 'Source: ' + SOURCE.as_posix(), '', 'Source SHA-256: `' + result['sha256'] + '`', '',
         'All 111 item frequency totals and weighted scores were checked against the PDF. No response counts were inferred or invented.', '',
         'The item SD columns below describe the dispersion of individual ratings for that item. They are not category or respondent-composite SDs.', '',
         'Overall group means use equal category weights. All calculations use unrounded values before display rounding.', '']
for group in groups:
    lines.extend(['## ' + group['name'], '', f"Respondents: {group['respondents']}. Overall equal-category mean: **{group['overall_category_mean']:.4f}**. Equal-item mean: {group['overall_item_mean']:.4f}.", '',
                  '| Category | Items | Mean | Interpretation |', '|---|---:|---:|---|'])
    for category in group['categories']:
        lines.append(f"| {category['name']} | {category['items']} | {category['mean']:.4f} | {category['interpretation']} |")
    lines.extend(['', '| Category / Item | Rating 4 | Rating 3 | Rating 2 | Rating 1 | n | Score | Mean | Sample SD | Population SD |',
                  '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|'])
    for item in group['items']:
        f4,f3,f2,f1 = item['frequencies']
        lines.append(f"| {item['category']} Q{item['number']} | {f4} | {f3} | {f2} | {f1} | {item['n']} | {item['score']} | {item['mean']:.4f} | {item['sample_sd']:.4f} | {item['population_sd']:.4f} |")
    lines.append('')
    lines.append('### Original Indicators')
    lines.extend(f"- {item['category']} Q{item['number']}: {item['question']}" for item in group['items'])
    lines.append('')
lines.extend(['## Calculation Corrections and Limits', ''] + ['- ' + note for note in result['notes']])
(ROOT/'Survey-Calculation-Audit.md').write_text('\n'.join(lines), encoding='utf-8')

out = ROOT/'survey-source-render'
out.mkdir(exist_ok=True)
doc = pdfium.PdfDocument(SOURCE)
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)
for start in range(0, 27, 9):
    sheet = Image.new('RGB', (1500, 2140), '#E5E7E9')
    draw = ImageDraw.Draw(sheet)
    for offset, page_index in enumerate(range(start, start+9)):
        picture = doc[page_index].render(scale=1.25).to_pil()
        picture.save(out/f'page-{page_index+1:02}.png')
        picture.thumbnail((470, 665))
        x,y = 15+(offset%3)*500, 35+(offset//3)*710
        sheet.paste(picture, (x,y))
        draw.text((x,y-24), f'Page {page_index+1}', font=font, fill='black')
    sheet.save(out/f'contact-{start//9+1}.png')
for group in groups:
    print(group['name'], group['respondents'], [(c['name'], round(c['mean'], 6)) for c in group['categories']], 'overall', group['overall_category_mean'])
print('Validated 111 survey items and rendered all 27 source pages.')
