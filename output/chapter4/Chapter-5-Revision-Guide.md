# Chapter 5 Revision Guide

Source: `C:\Users\justine mendoza\Documents\METRICS first batch\Final Draft Chapter 1-5 Thesis (2).pdf`.

The current full draft has 233 pages. Chapter 5 begins on printed page 168 and ends in the first paragraph of page 183, immediately before BIBLIOGRAPHY. All locations below refer to the current PDF, before replacement.

## What to Replace

Use the companion `Chapter-5-Revised-Text.md` as the complete replacement for Chapter 5, from its chapter title on page 168 through the final deployment sentence on page 183. Keep BIBLIOGRAPHY and everything after it. The supplied PDF has not been edited.

The replacement retains the existing chapter title and main heading sequence: Introduction, Summary of Findings, Presentation of Data, Interpretation of Results, Discussion, Recommendations, and Summary. It does not add new results or describe the system observation as revised.

## Page-by-Page Changes

| Current Location | Remove or Replace | Reason |
| --- | --- | --- |
| 168 | Introduction and opening Summary of Findings | Include the separate manual survey, manual writing baseline, observation protocol, and respondent-group boundaries |
| 169 | Performance Efficiency paragraph | DB average 15.42 ms, Fast 24.00/min, and manual 2.56/min are not the current reported summaries; unmatched rates cannot establish a complete-task advantage |
| 169 | Usability paragraph using 0.00% lockout | Correct recorded rate is 1.32%, deliberately triggered; it is not a direct usability score |
| 170 | Security paragraph | Replace 96.15% accuracy, 11.58% denial, eight tailgating records, and three irregular exits with the corrected definitions and counts |
| 168-171 | Other category paragraphs | Anchor claims to separate respondent-group means or documented functionality cases; do not infer unmeasured technical performance from perception ratings |
| 171-173 | Presentation of Data, Figure 23, and pooled interpretation | Remove the pooled radar chart and pooled category ranking; replace with respondent-group tables and interpretation |
| 173-177 | Discussion | Replace old numeric comparisons, retain configured-mode and tested-control distinctions, and explicitly address current SOP 6A, B, and C |
| 178-181 | Recommendations | Add matched task timing and direct workload/record-quality measurement; correct assumptions about physical movement and presumed alert overload; retain relevant future testing directions |
| 181-183 | Summary | Report the three separate system-group overall means and the survey t-test, without a single pooled ISO score or unmeasured throughput superiority |

## Main Numerical Replacements

| Old Chapter 5 Claim | Correct Reporting |
| --- | --- |
| Manual processing 23.47 seconds | Manual active writing mean 21.803 seconds, sample SD 4.436, n = 70 |
| Fast complete processing 2.50 seconds | Complete task duration unavailable; mean internal latency 11.49 ms |
| Standard complete processing 8.65 seconds | Complete task duration unavailable; mean PIN interaction 2.505 seconds and internal latency 57.37 ms |
| High-Security complete processing 14.18 seconds | Complete task duration unavailable; mean PIN + QR interaction 5.799 seconds and internal latency 54.06 ms |
| Fast / Standard / High-Security throughput 24.00 / 6.94 / 4.23 students/min | Supplementary recorded-entry rates 21.98 / 7.46 / 4.95 intervals/min, not matched overall throughput |
| Manual throughput 2.56 students/min | Derived writing service-time-equivalent rate 2.75 completions/min, not observed continuous queue throughput |
| Single average DB query time 15.42 ms | If discussed, use the Chapter 4 mode-specific means: Fast 2.88, Standard 47.95, High-Security 44.90 ms; do not add DB time to overlapping internal latency |
| Authentication accuracy 96.15% | Observed labelled applicable credential-decision accuracy 100.00% (222/222), including correct rejections |
| Denial rate 11.58% | Record-based denial proportion 4.11% (18/438), with reset exits and deliberate challenges in the record mix |
| PIN lockout 0.00% | Two deliberate triggers in 152 submissions: 1.32% |
| Eight anti-tailgating records | Five recorded consecutive-entry/state violations |
| Three irregular exit records | One recorded irregular-exit condition |
| Pooled Compatibility 3.90, Functional Suitability 3.84, Reliability 3.74, Performance Efficiency 3.79 | Do not use pooled means; report the separate group/category means in the replacement table |
| One overall ISO system assessment | Students 3.74, security personnel 3.63, faculty/event-organizer/administrator group 3.81, separately interpreted |
| No survey inferential result in the conclusion | Add Welch t = 15.124, df = 89.076, p < .001, mean difference 1.458, 95% CI [1.267, 1.650] |

The manual and automated durations measure different tasks or stages. Do not substitute the new component timings into the old sentences claiming a speed multiplier or complete-task improvement.

## Figure and Table Handling

Remove the current pooled **Figure 23: Overall ISO/IEC 25010 System Evaluation Summary** on page 172, including the graphic and caption. Its single polygon suggests a combined category score that Chapter 4 explicitly does not report.

The revised text replaces that figure with two tables:

- **Table 44. Summary of Category Means by Respondent Group**.
- **Table 45. Summary of Observation and Inferential Findings**.

These numbers continue the current Chapter 4 sequence, which ends at Table 43 on page 156. The uncaptioned worked-input table on page 155 is not counted in this current sequence. If that input table is subsequently given its own caption, renumber subsequent tables consistently rather than creating duplicate captions.

There is no need to retain a Chapter 5 chart merely for decoration. If a chart is later required, it must display separate respondent series with unassessed categories omitted; it must not restore the pooled scores. Update the list of figures after removing Figure 23 and the list of tables after insertion.

## Interpretation Boundaries Retained

- Seventy participants tested three modes; 210 successful entries are repeated trials across modes, not 210 distinct people.
- Reused test credentials demonstrate configured credential behavior, not exclusive physical identity or live enrollment verification for all participants.
- Correct rejection contributes to credential-decision correctness; deliberate challenges are not ordinary user failures.
- Internal latency, PIN/QR interaction, manual writing, and between-entry spacing have different measurement boundaries.
- The survey t-test compares different respondent groups using process-specific items and is not proof of causal improvement.
- Security personnel and faculty/event-organizer/administrator composite SDs remain unavailable from item-frequency totals.
- QR matching is a credential-integrity check, not an independently established third factor or comprehensive clone-detection mechanism.
- Logical entry-state enforcement is not independent proof of physical tailgating prevention.
- The listed offline cases show tested outcomes, not guaranteed operation under every outage.
- Survey ratings are not formal ISO certification or an independent code, portability, or security audit.

## Recommendations Changed for Evidence Consistency

The old recommendation asserted that physical movement contributed more to transaction duration than DB processing. Physical movement was not independently timed. The revised recommendation instead calls for direct investigation of the longer recorded interaction stages.

The old recommendation assumed rapid alerts increased personnel cognitive workload. The supplied ratings do not establish that cause. The revised recommendation calls for targeted interface and workload evaluation rather than presenting overload as a measured result.

The old recommendations for broader testing, credential integrity, offline resilience, PIN security, deployment assessment, and optional future modules are retained in updated wording. The replacement gives priority to missing matched timing and throughput evidence and to direct security, record-quality, and workload measures.

## Final Integration Checks

1. Replace Chapter 5 only; preserve BIBLIOGRAPHY and appendices.
2. Insert the two replacement tables as editable tables, not screenshots of the pooled chart.
3. Repeat table column headings if a table spans pages, and keep rows together where possible.
4. Preserve units: ms for internal latency, seconds for user interaction and manual writing, and intervals/min for the log-derived rates.
5. Keep mean and SD rounding consistent with Chapter 4.
6. Regenerate the table of contents and table/figure lists after final pagination.
7. Check Chapter 3's remaining processing-time, throughput, and SD wording for consistency. Correcting Chapter 5 does not itself fix the older statements still present in Chapter 3.

The new draft's student Performance Efficiency footer, manual n = 70 heading, comparison section number 4.5.6, and SOP 6 survey subsection show the requested corrections. This Chapter 5 task is not a fresh full audit of every Chapter 4 page.

No raw observation records, respondent answers, or supplied PDF files were modified.
