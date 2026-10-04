import hashlib
import json
import math
from pathlib import Path
from statistics import mean, stdev

import openpyxl


def beta_fraction(a, b, x):
    qab, qap, qam = a + b, a + 1, a - 1
    c = 1.0
    d = 1 - qab * x / qap
    d = 1 / (d if abs(d) > 1e-300 else 1e-300)
    result = d
    for m in range(1, 10001):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1 + aa * d
        c = 1 + aa / c
        d = 1 / (d if abs(d) > 1e-300 else 1e-300)
        c = c if abs(c) > 1e-300 else 1e-300
        result *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1 + aa * d
        c = 1 + aa / c
        d = 1 / (d if abs(d) > 1e-300 else 1e-300)
        c = c if abs(c) > 1e-300 else 1e-300
        delta = d * c
        result *= delta
        if abs(delta - 1) < 3e-14:
            return result
    raise ArithmeticError('Incomplete beta fraction did not converge')


def regularized_beta(x, a, b):
    if x <= 0:
        return 0.0
    if x >= 1:
        return 1.0
    scale = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
                     + a * math.log(x) + b * math.log1p(-x))
    if x < (a + 1) / (a + b + 2):
        return scale * beta_fraction(a, b, x) / a
    return 1 - scale * beta_fraction(b, a, 1 - x) / b


def two_sided_p(t, df):
    return regularized_beta(df / (df + t * t), df / 2, 0.5)


def critical_t(df, alpha=0.05):
    low, high = 0.0, 2.0
    while two_sided_p(high, df) > alpha:
        high *= 2
    for _ in range(120):
        mid = (low + high) / 2
        if two_sided_p(mid, df) > alpha:
            low = mid
        else:
            high = mid
    return (low + high) / 2


# Check distribution calculations against closed-form cases and a standard t critical value.
for t in (0.1, 1, 2, 20):
    assert abs(two_sided_p(t, 1) - (1 - 2 * math.atan(t) / math.pi)) < 1e-12
    assert abs(two_sided_p(t, 2) - (1 - t / math.sqrt(t * t + 2))) < 1e-12
assert abs(critical_t(10) - 2.2281388519649385) < 1e-10

folder = Path(__file__).resolve().parent
source = folder / 'Survey-Response-Entry-Template.xlsx'
wb = openpyxl.load_workbook(source, data_only=False)
categories = [
    ('Functional Suitability', 0, 5), ('Performance Efficiency', 5, 9),
    ('Usability', 9, 14), ('Reliability', 14, 19), ('Security', 19, 24),
]
groups = {}
for name in ('Manual', 'System'):
    rows = [[wb[name].cell(r, c).value for c in range(2, 26)] for r in range(2, 72)]
    assert all(type(v) in (int, float) and v in (1, 2, 3, 4) for row in rows for v in row)
    scores = {label: [mean(row[start:end]) for row in rows] for label, start, end in categories}
    scores['Overall'] = [mean(scores[label][i] for label, _, _ in categories) for i in range(70)]
    groups[name] = {
        'n': len(rows), 'scores': scores,
        'descriptive': {label: {'mean': mean(values), 'sample_sd': stdev(values)} for label, values in scores.items()},
    }

manual, system = (groups[name]['descriptive']['Overall'] for name in ('Manual', 'System'))
n1, n2 = groups['Manual']['n'], groups['System']['n']
v1, v2 = manual['sample_sd'] ** 2 / n1, system['sample_sd'] ** 2 / n2
difference = system['mean'] - manual['mean']
se = math.sqrt(v1 + v2)
t = difference / se
df = (v1 + v2) ** 2 / (v1 ** 2 / (n1 - 1) + v2 ** 2 / (n2 - 1))
p = two_sided_p(t, df)
# Independently integrate the t-density tail after substituting x = t/u.
steps = 32768
def normalized_tail(u):
    return 0.0 if u == 0 else u ** (df - 1) * ((df + t * t) / (df * u * u + t * t)) ** ((df + 1) / 2)
integral = (normalized_tail(1) + sum((4 if i % 2 else 2) * normalized_tail(i / steps)
                                    for i in range(1, steps))) / (3 * steps)
log_density = (math.lgamma((df + 1) / 2) - math.lgamma(df / 2)
               - 0.5 * math.log(df * math.pi) - (df + 1) / 2 * math.log1p(t * t / df))
integrated_p = 2 * t * math.exp(log_density) * integral
assert abs(integrated_p / p - 1) < 1e-9, (p, integrated_p)
critical = critical_t(df)
test = {'difference_system_minus_manual': difference, 'standard_error': se, 't': t, 'df': df,
        'p_two_sided': p, 'alpha': 0.05, 'critical_t': critical,
        'ci95': [difference - critical * se, difference + critical * se]}
result = {'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
          'scoring': 'Equal mean of five respondent-level category means, without intermediate rounding.',
          'groups': groups, 'welch_overall': test}
(folder / 'completed-survey-analysis.json').write_text(json.dumps(result, indent=2), encoding='utf-8')

def interpretation(value):
    return 'Highly Satisfactory' if value >= 3.26 else 'Satisfactory' if value >= 2.51 else 'Unsatisfactory' if value >= 1.76 else 'Highly Unsatisfactory'

lines = [
    '# Survey Mean Comparison for Chapters 3 and 4', '',
    '## Chapter 3: Statistical Treatment of Data', '',
    'A two-tailed Welch independent-samples t-test was used to compare the overall survey ratings of the manual attendance process and the proposed system at a 0.05 level of significance. The manual and proposed-system groups consisted of 70 different student respondents each; therefore, the responses were analyzed as independent samples rather than paired observations. Each respondent\'s score for a category was computed as the mean of the answers within that category. The overall score was then computed as the equally weighted mean of the five category scores: Functional Suitability, Performance Efficiency, Usability, Reliability, and Security. Performance Efficiency contained four items, whereas each of the other categories contained five items. Calculations used unrounded respondent scores.', '',
    'The null hypothesis stated that the population mean overall survey ratings of the manual process and proposed system were equal. The alternative hypothesis stated that the population means differed. Welch\'s procedure was selected because it does not require the two groups to have equal variances. Sample standard deviations used the n - 1 denominator. The t statistic, Welch-Satterthwaite degrees of freedom, two-tailed p-value, mean difference, and 95% confidence interval were reported. The null hypothesis was rejected when p < 0.05.', '',
    'The unit of analysis was the respondent, not an individual questionnaire item or an aggregate category mean. The comparison concerns student-reported evaluations across corresponding quality categories. The questionnaires used method-specific wording, so the results were not interpreted as a comparison of identical item wording, objective processing times, or a causal within-person improvement.', '',
    '## Chapter 4: Comparison of Manual and Proposed-System Survey Ratings', '',
    '### Descriptive Results', '',
    '| Category | Manual Mean | Manual Sample SD | Proposed-System Mean | Proposed-System Sample SD |',
    '| --- | ---: | ---: | ---: | ---: |',
]
for label in [c[0] for c in categories] + ['Overall']:
    a, b = (groups[name]['descriptive'][label] for name in ('Manual', 'System'))
    lines.append(f'| {label} | {a["mean"]:.3f} | {a["sample_sd"]:.3f} | {b["mean"]:.3f} | {b["sample_sd"]:.3f} |')
lines += [
    '', 'Note: n = 70 in each group. SD denotes the sample standard deviation of respondent-level category or overall scores, not an average of item-level standard deviations.', '',
    f'The manual attendance process obtained an overall mean rating of {manual["mean"]:.3f} (SD = {manual["sample_sd"]:.3f}), interpreted as {interpretation(manual["mean"])}. The proposed system obtained an overall mean rating of {system["mean"]:.3f} (SD = {system["sample_sd"]:.3f}), interpreted as {interpretation(system["mean"])}. The proposed-system mean exceeded the manual-process mean by {difference:.3f} points on the four-point response scale.', '',
    '### Independent-Samples t-Test', '',
    '| Comparison | Mean Difference (System - Manual) | t | Welch df | Two-Tailed p | 95% CI for Difference | Decision at alpha = 0.05 |',
    '| --- | ---: | ---: | ---: | --- | --- | --- |',
    f'| Overall survey score | {difference:.3f} | {t:.3f} | {df:.3f} | < .001 | [{test["ci95"][0]:.3f}, {test["ci95"][1]:.3f}] | Reject H0 |', '',
    f'The two-tailed Welch independent-samples t-test showed a statistically significant difference between the groups, t({df:.2f}) = {t:.2f}, p < .001. The mean difference favoring the proposed system was {difference:.3f} points, with a 95% confidence interval from {test["ci95"][0]:.3f} to {test["ci95"][1]:.3f}. Therefore, the null hypothesis of equal mean overall survey ratings was rejected at the 0.05 significance level.', '',
    'These findings indicate that the students who evaluated the proposed system reported higher overall ratings than the separate group who evaluated the manual attendance process. The inference is limited to the survey scores obtained under the study conditions. Since the two groups comprised different respondents and the questionnaires used method-specific wording, the difference does not by itself establish a causal improvement, identical measurement across instruments, or a reduction in complete-task processing time.', '',
    '### Placement and Reporting Notes', '',
    '- Add the Chapter 3 paragraphs to Statistical Treatment of Data after descriptive survey scoring.',
    '- Place the Chapter 4 tables and interpretation after the student survey evaluation and manual survey discussion.',
    '- Use the descriptive values above wherever these student/manual category and overall scores are reported. Administrator and security-personnel results are outside this comparison.',
    '- Report p < .001 in the thesis, not p = .000. The numerical p-value is retained in the analysis record.',
    '- This analysis contains one primary test of overall survey scores. Category rows are descriptive and are not presented as five additional hypothesis tests.',
    '- The existing manual writing duration and automated component durations use different timing boundaries. No processing-time t-test is included in this survey result.', '',
    '## Calculation Record', '',
    f'Source: `{source}`',
    f'SHA-256: `{result["sha256"]}`',
    'Both groups contain 70 complete records and 1,680 valid answers each. No source answers were changed.',
    f'Full-precision two-sided p-value: {p:.12g}.', '',
    f'The p-value was independently checked by numerical integration of the t-density tail ({integrated_p:.12g}). The mean difference and t statistic also agree with an independent spreadsheet formula-engine calculation. The native Excel application could not be started, so its T.TEST result was not verified in this environment.', '',
    'Excel cross-check formula for the completed template: `=T.TEST(Manual!AE2:AE71,System!AE2:AE71,2,3)`.', '',
    'Method reference: [NIST, Two-Sample t-Test for Equal Means](https://www.itl.nist.gov/div898/handbook/eda/section3/eda353.htm).',
    'Excel reference: [Microsoft, T.TEST function](https://support.microsoft.com/en-us/excel/functions/t-test-function).', '',
]
output = folder / 'Chapter-3-and-4-Survey-T-Test.md'
output.write_text('\n'.join(lines), encoding='utf-8')
print(json.dumps({'groups': {name: data['descriptive'] for name, data in groups.items()}, 'test': test, 'report': str(output)}, indent=2))
