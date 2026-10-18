# Latest Chapter 4 Accuracy Review

Source: `C:\Users\justine mendoza\Documents\METRICS first batch\Chapter 4 Thesis Update (1).pdf`

Reviewed all 62 pages through text extraction, visually inspected the principal figures, numerical tables, and worked t-test, and cross-checked numerical results against the previously supplied calculation records. Page references below are the page numbers printed in this PDF, not the older full thesis. The PDF was not edited.

## Verdict

The observation tables, manual timing summary, respondent-level survey comparison, and Welch t-test are numerically consistent with the available calculation records. The chapter is not yet fully consistent or ready for submission: several survey interpretations and footer SDs remain outdated, administrator demographics remain unresolved, and section/table/figure numbering is duplicated.

This review verifies numerical consistency and reporting boundaries. It does not independently verify the experiment, the five administrator demographic forms, actual hardware wiring, or the original administered questionnaire wording.

## Priority 1: Incorrect or Misleading Statistical Reporting

### 1. Student interpretation still uses older values: page 22

The category table on pages 21-22 is correct, but its following interpretation is not.

| Entry | Currently in narrative | Correct value |
| --- | ---: | ---: |
| Overall mean | 3.76 | 3.74 |
| Functional Suitability | 3.77 | 3.76 |
| Performance Efficiency | 3.76 | 3.74 |
| Usability | 3.75 | 3.74 |
| Reliability | 3.73 | 3.71 |
| Security | 3.79 | 3.75 |
| Identity-confirmation item | 3.90 | 3.84 |
| Lowest item: unexpected failures/freezing | 3.66 | 3.60 |

Functional Suitability, not Security, has the highest category mean.

Replacement:

> Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.74. Functional Suitability received the highest category mean of 3.76, followed by Security at 3.75. Performance Efficiency and Usability each recorded 3.74, while Reliability recorded 3.71. At item level, the identity-confirmation statement received 3.84, while the lowest item mean was 3.60 for processing without unexpected failures or freezing. These ratings describe perceived quality within the evaluation setting and do not independently establish resistance to credential copying or impersonation.

### 2. Administrator summary contradicts its category means: pages 32-33 and 41

- Page 32: Performance Efficiency contains **5**, not 4, questions.
- Page 32: Compatibility contains **4**, not 5, questions.
- Page 33: Overall mean should be **3.81**, not 3.63.
- Page 41: The all-category overall row should consistently display **3.81**, not 3.8.
- Total item count **39** is correct.

The equal mean of the eight category means is:

`(3.80 + 3.68 + 3.75 + 3.88 + 3.76 + 3.84 + 3.88 + 3.92) / 8 = 3.81375`, reported as **3.81**.

The interpretation on page 33 and later discussion already use 3.81; retain them.

### 3. Category and overall SD footers are not composite SDs: pages 23-46

The detailed tables still label averages of item SDs as category or overall SDs. These are different statistics. This contradicts the explanation on page 21 that role-group composite SDs are not reported.

Examples:

- Student overall SD on page 26: **0.47** is not the respondent-level overall SD. Use **0.290**.
- Manual overall SD on page 46: **1.01** is not the respondent-level overall SD. Use **0.753**.
- Security-personnel overall SD on page 32: **0.42** is an average of item SDs, not an established overall-score SD.
- Administrator overall SD on page 41: **0.32** is an average of item SDs, not an established overall-score SD.

Preserve individual-question SDs. In category/overall footer rows, either omit the SD or replace it with the appropriate respondent-level sample SD below.

| Category | Student-system composite sample SD | Manual composite sample SD |
| --- | ---: | ---: |
| Functional Suitability | 0.318 | 0.899 |
| Performance Efficiency | 0.366 | 0.919 |
| Usability | 0.329 | 0.854 |
| Reliability | 0.348 | 0.852 |
| Security | 0.341 | 0.883 |
| Overall equal-category score | 0.290 | 0.753 |

For security personnel and administrators, use a dash with a note that respondent-level composite SDs are unavailable from the supplied item-frequency totals. Individual respondent records are required to calculate them.

Do not change legitimate item SDs such as **0.55** to **0.49**. The current role-group sample SD ranges are consistent with the N - 1 convention.

### 4. SD notes still say N: pages 22 and 27

Change "using N in the denominator" to **"using N - 1 in the denominator"**.

The page 22 note also implies that student composite SDs are unavailable, although individual student records were available and page 46 reports those composite SDs.

Replacement for the student summary note:

> The SD range gives the minimum and maximum individual-item sample SD within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall means give equal weight to the five categories. Respondent-level category and overall sample SDs are reported in the manual/system comparison table.

Replacement for the security-personnel summary note:

> The SD range gives the minimum and maximum individual-item sample SD within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall means give equal weight to the five categories. Respondent-level composite SDs were not calculated because only item-frequency totals were available for this group.

### 5. Administrator demographics do not account for all five respondents: pages 18-19

The role tally totals five respondents, but years of service totals only four: two in 11-15 years and two in 15-20 years. The stated 50% + 50% uses a denominator of four without explaining it.

Check the original forms before correcting this:

- If the fifth answer was omitted, add it to the correct category and recalculate percentages.
- If one person did not answer, report valid n = 4 and one missing response. The 50% figures may then describe valid responses only.
- If using all five respondents as the denominator and nonresponse is confirmed, the two existing categories are 40% each and missing is 20%.

The intervals 11-15 and 15-20 overlap at 15. Check the original choices. Do not silently reassign responses or invent a fifth answer.

The narrative on page 19 must match the corrected tally and stated denominator.

## Priority 2: Structure, Captions, and Evidence

### 6. Manual sample heading is truncated: page 41

Replace **"4.5.4 Manual Process Student Pre-Evaluation (n=7"** with the full heading and **n = 70**. Adjust its section number after fixing the duplicated student-evaluation number.

### 7. Duplicate and missing section numbers

- Page 2 jumps from 4.1 directly to 4.3.2. Restore the missing structure or renumber the retained sections consecutively.
- Pages 20-21 use 4.5.1 for both Scoring and Interpretation and Student Evaluation.
- Page 50 uses 4.6 for both Discussion and Interpretation of System Performance; the latter should be a subsection.
- Pages 51 and 53 duplicate 4.6.2.
- Pages 51 and 55 duplicate 4.6.3.
- Pages 52 and 56 duplicate 4.6.4.

Recommended user-evaluation sequence:

1. 4.5.1 Scoring and Interpretation
2. 4.5.2 Student Evaluation
3. 4.5.3 Security Personnel Evaluation
4. 4.5.4 Faculty/Event Organizer/Administrator Evaluation
5. 4.5.5 Manual Process Student Pre-Evaluation
6. 4.5.6 Comparison of Manual and Proposed-System Survey Ratings

Recommended discussion sequence after consolidating repetitive material:

1. 4.6.1 Interpretation of System Performance
2. 4.6.2 Comparison with the Manual Attendance Process
3. 4.6.3 Interpretation of Security Outcomes
4. 4.6.4 System Strengths and Practical Advantages
5. 4.6.5 Implications of the Study
6. 4.6.6 Comparison with Related Studies
7. 4.7 Summary

Merge Interpretation of Manual and Automated Measurements (pages 51) into Comparison with the Manual Attendance Process (pages 53-55). Merge Practical Advantages and Their Limits (pages 52-53) into System Strengths and Practical Advantages (pages 55-56). Preserve unique limitations and interpretations instead of deleting evidence-bearing material wholesale.

The summary is now at the end, which is correct. The new implications and related-study comparisons on pages 56-61 avoid the obsolete speed claims and can be retained.

### 8. Table and figure numbers are still duplicated

- Page 50 still labels the t-test table **Table 999**.
- Table numbers 7-14 and others identify multiple unrelated tables.
- Figure 20 labels both the system-timing figure on page 6 and the survey-comparison figure on page 47.

Number every table and figure uniquely in order of appearance, continuing the full thesis sequence if that is the institutional convention. Update in-text references and the lists of tables/figures. Do not change a single caption in isolation while leaving references stale.

### 9. Functional-test results are promised but not presented in this chapter

The introduction on page 2 says that functional testing results are presented, but the document proceeds to system-observation metrics without a distinct functional-test-results table. Later text mentions documented inactive/expelled tests.

Restore the actual documented test outcomes or give a precise appendix/table reference to where they appear. The Appendix E reference currently describes engineering hurdles; it does not, by itself, identify the missing functional-test evidence. Do not invent test outcomes.

### 10. Prototype architecture wording needs a hardware check: page 2

The wording can imply that the PIN mechanism and QR camera are integrated into the ESP32 hardware and communicate through its serial link. The current QR-scanner implementation captures a Windows video device through MediaCapture, while MySQL and Firestore are implemented in the repository.

Clarify the actual tested wiring: ESP32/PN532 serial NFC capture versus a computer-connected/integrated webcam and the application PIN interface, if that matches the tested setup. The software alone does not establish the physical wiring used in August.

## Verified Results to Keep

- 438 exported records: 210 granted entries, 210 granted reset exits, and 18 denied records.
- Mode totals: Fast 142, Standard 152, High-Security 144.
- Seventy successful entry trials per mode, not 210 different people.
- Internal latency means: 11.49, 57.37, and 54.06 ms.
- Recorded PIN/QR interaction means: Standard 2.505 s; High-Security 5.799 s.
- Recorded-entry rates: 21.98, 7.46, and 4.95 intervals/min, correctly qualified as supplementary.
- 66 within-round intervals per mode; 19 + 19 + 19 + 9 is correct.
- Observed labelled credential-decision accuracy: 222/222 = 100.00%, correctly limited to the recorded challenges.
- Recorded denial rate: 18/438 = 4.11%.
- Recorded PIN lockout rate: 2/152 = 1.32%; Standard 2/79 = 2.53%.
- Manual writing summary: n = 70, sum = 1,526.24 s, mean = 21.803 s, median = 21.135 s, sample SD = 4.436 s, range = 13.21-37.63 s.
- Respondent-level descriptive comparison on page 46.
- Worked computation on page 49: rounded-input SE = 0.096445 and t approximately 15.12.
- Final Welch results: t = 15.124, df = 89.076, p < .001, difference = 1.458, 95% CI [1.267, 1.650].

The final t and df were recomputed from the stored respondent scores and agree with the reported values. Using only rounded printed means and SDs cannot reproduce every final decimal; the current rounding explanation is appropriate.

## Methodological Boundary

The current discussion accurately states that complete automated task duration was not independently measured using a boundary comparable to manual writing. It also accurately avoids claiming measured reductions in human error, proxy attendance, or fraud. These are unresolved empirical portions of a matched objective comparison, not errors that can be fixed by changing prose or applying a t-test to unlike durations.

Accordingly, the chapter can report the available findings and limitations, but should not claim that every objective comparison required by SOP 6 has been fully demonstrated. Additional comparable measurements or an independent record/challenge audit would be required for those claims.

## Layout

The page 49 formulas render correctly. The observed charts match the reported values. Some table rows still split across pages, including the student category summary on pages 21-22, and continued tables lack repeated headers. Keep rows together where feasible, repeat continued-table headers, and use consistent two-decimal mean reporting in descriptive item/category tables and three-decimal reporting in inferential tables.

The source PDF remains unchanged. No raw response or observation data were changed during this review.
