# Survey Mean Comparison for Chapters 3 and 4

## Chapter 3: Statistical Treatment of Data

A two-tailed Welch independent-samples t-test was used to compare the overall survey ratings of the manual attendance process and the proposed system at a 0.05 level of significance. The manual and proposed-system groups consisted of 70 different student respondents each; therefore, the responses were analyzed as independent samples rather than paired observations. Each respondent's score for a category was computed as the mean of the answers within that category. The overall score was then computed as the equally weighted mean of the five category scores: Functional Suitability, Performance Efficiency, Usability, Reliability, and Security. Performance Efficiency contained four items, whereas each of the other categories contained five items. Calculations used unrounded respondent scores.

The null hypothesis stated that the population mean overall survey ratings of the manual process and proposed system were equal. The alternative hypothesis stated that the population means differed. Welch's procedure was selected because it does not require the two groups to have equal variances. Sample standard deviations used the n - 1 denominator. The t statistic, Welch-Satterthwaite degrees of freedom, two-tailed p-value, mean difference, and 95% confidence interval were reported. The null hypothesis was rejected when p < 0.05.

The unit of analysis was the respondent, not an individual questionnaire item or an aggregate category mean. The comparison concerns student-reported evaluations across corresponding quality categories. The questionnaires used method-specific wording, so the results were not interpreted as a comparison of identical item wording, objective processing times, or a causal within-person improvement.

## Chapter 4: Comparison of Manual and Proposed-System Survey Ratings

### Descriptive Results

| Category | Manual Mean | Manual Sample SD | Proposed-System Mean | Proposed-System Sample SD |
| --- | ---: | ---: | ---: | ---: |
| Functional Suitability | 2.483 | 0.899 | 3.763 | 0.318 |
| Performance Efficiency | 1.932 | 0.919 | 3.743 | 0.366 |
| Usability | 2.637 | 0.854 | 3.740 | 0.329 |
| Reliability | 2.183 | 0.852 | 3.711 | 0.348 |
| Security | 2.183 | 0.883 | 3.751 | 0.341 |
| Overall | 2.284 | 0.753 | 3.742 | 0.290 |

Note: n = 70 in each group. SD denotes the sample standard deviation of respondent-level category or overall scores, not an average of item-level standard deviations.

The manual attendance process obtained an overall mean rating of 2.284 (SD = 0.753), interpreted as Unsatisfactory. The proposed system obtained an overall mean rating of 3.742 (SD = 0.290), interpreted as Highly Satisfactory. The proposed-system mean exceeded the manual-process mean by 1.458 points on the four-point response scale.

### Independent-Samples t-Test

| Comparison | Mean Difference (System - Manual) | t | Welch df | Two-Tailed p | 95% CI for Difference | Decision at alpha = 0.05 |
| --- | ---: | ---: | ---: | --- | --- | --- |
| Overall survey score | 1.458 | 15.124 | 89.076 | < .001 | [1.267, 1.650] | Reject H0 |

The two-tailed Welch independent-samples t-test showed a statistically significant difference between the groups, t(89.08) = 15.12, p < .001. The mean difference favoring the proposed system was 1.458 points, with a 95% confidence interval from 1.267 to 1.650. Therefore, the null hypothesis of equal mean overall survey ratings was rejected at the 0.05 significance level.

These findings indicate that the students who evaluated the proposed system reported higher overall ratings than the separate group who evaluated the manual attendance process. The inference is limited to the survey scores obtained under the study conditions. Since the two groups comprised different respondents and the questionnaires used method-specific wording, the difference does not by itself establish a causal improvement, identical measurement across instruments, or a reduction in complete-task processing time.

### Placement and Reporting Notes

- Add the Chapter 3 paragraphs to Statistical Treatment of Data after descriptive survey scoring.
- Place the Chapter 4 tables and interpretation after the student survey evaluation and manual survey discussion.
- Use the descriptive values above wherever these student/manual category and overall scores are reported. Administrator and security-personnel results are outside this comparison.
- Report p < .001 in the thesis, not p = .000. The numerical p-value is retained in the analysis record.
- This analysis contains one primary test of overall survey scores. Category rows are descriptive and are not presented as five additional hypothesis tests.
- The existing manual writing duration and automated component durations use different timing boundaries. No processing-time t-test is included in this survey result.

## Calculation Record

Source: `C:\Users\justine mendoza\source\repos\NFC_System\output\chapter4\Survey-Response-Entry-Template.xlsx`
SHA-256: `6fbfb6019dd554a6c0350dd432b6786c74857ecbfabf82856eb0013097dbb4a2`
Both groups contain 70 complete records and 1,680 valid answers each. No source answers were changed.
Full-precision two-sided p-value: 2.46553903765e-26.

The p-value was independently checked by numerical integration of the t-density tail (2.46553903765e-26). The mean difference and t statistic also agree with an independent spreadsheet formula-engine calculation. The native Excel application could not be started, so its T.TEST result was not verified in this environment.

Excel cross-check formula for the completed template: `=T.TEST(Manual!AE2:AE71,System!AE2:AE71,2,3)`.

Method reference: [NIST, Two-Sample t-Test for Equal Means](https://www.itl.nist.gov/div898/handbook/eda/section3/eda353.htm).
Excel reference: [Microsoft, T.TEST function](https://support.microsoft.com/en-us/excel/functions/t-test-function).
