# Chapter 4 Removal and Revision Checklist

Reviewed source: `C:\Users\justine mendoza\Documents\METRICS first batch\Chapter 4 Thesis Update.pdf`

Page references below are the current PDF page numbers, including its title page. The source PDF was not edited. The corrected manual and student survey results in the main tables should be retained. The central problem is that older interpretations and comparison passages remain alongside them.

## 1. Remove Outdated or Incorrect Passages

### Pages 52-54: Old "4.6.2 Comparison with Existing Systems"

Remove this subsection from its heading on page 52 to the next heading, "4.6.2.1 Vulnerability to Common Access and Attendance Risks," on page 54. Do not remove the correct continuation of the Chapter 4 summary at the top of page 52.

The subsection reintroduces an older six-person manual baseline and unsupported full-task automated estimates:

- 6 manual participants; total 140.81 seconds; mean 23.47 seconds; rate 2.56 students/minute.
- Estimated automated transaction times of 2.50, 8.65, and 14.18 seconds.
- Rates of 24.00, 6.94, and 4.23 students/minute.
- Manual-versus-system multipliers of 9.38, 2.71, and 1.66.

These conflict with the retained 70-person manual writing dataset and the limitations already stated on pages 4, 7-8, and 15. Do not simply replace these rates with 21.98, 7.46, and 4.95 and retain the superiority claim: those are log-derived between-entry rates, not matched complete-task throughput.

### Pages 55-56: Unsupported throughput paragraph

Remove the paragraph beginning "Furthermore, the system demonstrated a higher observed throughput than the traditional manual procedure..." and its continuation on page 56 ending with the caution about all real-world campus conditions. It uses the old 24.00, 2.56, and 4.23 figures.

Retain the separate paragraphs about configurable credential requirements and potential digital-record/offline capabilities, but place them in the single retained discussion section.

### Page 57: Old efficiency-comparison paragraph

Remove the first paragraph beginning "The throughput results further indicate that the system can improve the efficiency..." It repeats the old 24.00, 6.94, 4.23, and 2.56 rates and claims improved processing capacity from non-comparable measurements.

### Pages 58-61: Unsupported claims about this study inside related-study discussion

Do not delete the entire related-studies discussion. Remove or rewrite only the present-study claims that depend on the old estimates:

- Page 58, Ajayi comparison: remove the assertion that this study demonstrated a similar measured faster-processing pattern and the 2.50-second Fast estimate.
- Page 59, Singh comparison: remove the 2.50/8.65/14.18-second comparison and do not equate a configured mode selection with an automatically calculated risk-score framework.
- Pages 60-61, Overall Comparison: remove the 24.00-versus-2.56 comparison and the claim that all modes demonstrated higher matched throughput.

Retain literature-specific claims only after checking them against Chapter 2 and the actual cited papers. External publication claims were not independently verified in this document review.

### Page 14: Misplaced PIN-lockout paragraph

Under "Average Processing Time per Student," remove the paragraph beginning "The researcher confirmed that both PIN_LOCKED records..." It is copied from the lockout section and does not explain manual-versus-system timing. Keep that information in the actual PIN Lockout Rate subsection.

### Pages 23-46: Misleading overall/category SD entries

Do not delete the legitimate individual-item SD values. Remove or replace the SD values in the rows labelled "Overall Weighted Mean and SD" and "OVERALL SYSTEM WEIGHTED MEAN (All Categories)." These values average item SDs rather than measure variation among respondent composite scores.

For students and manual respondents, use the actual respondent-level SDs listed in Section 3 below. For security personnel and administrators, leave the category/overall composite SD unavailable unless individual response records are supplied. An average of item SDs must not be labelled simply as a category or overall SD.

## 2. Revise Numeric Tables, Labels, and Notes

### Page 5: Internal-latency table

Change the Fast mean from **11.4 to 11.49 ms** and Standard from **57.3 to 57.37 ms**. The narrative immediately below already uses the correct two-decimal values. Keep High-Security at 54.06 ms.

### Page 9: Credential-decision accuracy table

Rename "Expected-Invalid Grants" to **"Expected-Invalid Denials."** Invalid attempts were rejected, not granted. Change the Total row's expected-valid count from **70 to 210**.

| Mode | Expected-Valid Grants | Expected-Invalid Denials | Applicable Decisions | Correct Decisions | Accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Fast | 70 | 0 | 70 | 70 | 100.00% |
| Standard | 70 | 9 | 79 | 79 | 100.00% |
| High-Security | 70 | 3 | 73 | 73 | 100.00% |
| Total | 210 | 12 | 222 | 222 | 100.00% |

Keep the caution that this is researcher-labelled correctness for the recorded configured-credential sample, not universal authentication accuracy or independent proof of each person's identity.

### Page 11: PIN submission table

The numeric rows were given the wrong mode labels. The first displayed numeric row belongs to Standard, the second to High-Security, and the last to Total. Fast had no PIN submissions.

| Mode | PIN Stages in Granted Entries | PIN_FAILURE | Triggered PIN_LOCKED | PIN Passed Then QR Denied | Total PIN Submissions | Lockout Rate |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Fast | 0 | 0 | 0 | 0 | 0 | N/A |
| Standard | 70 | 7 | 2 | 0 | 79 | 2.53% |
| High-Security | 70 | 1 | 0 | 2 | 73 | 0.00% |
| Total | 140 | 8 | 2 | 2 | 152 | 1.32% |

Fast's rate is N/A because the denominator is zero, not an observed 0% PIN-lockout rate.

### Pages 13 and 15: Copied table captions

- Page 13: replace "Recorded Pin Submissions and Lockout Rate" with **"Manual Paper Attendance Writing Duration."**
- Page 15: replace the same copied caption with **"Available Timing Measurements and Measurement Boundaries."**

The actual PIN submissions/lockout caption belongs only to the table on page 11.

### Page 14: Replacement timing-comparison paragraph

Insert this in place of the copied PIN-lockout paragraph:

> The manual observation measured active attendance-writing duration from the first written character to completion of the required information, with a mean of 21.803 seconds. The automated exports measured internal system latency and recorded PIN/QR interaction stages, but did not provide independently timed complete NFC-tap-to-finished-verification durations. The manual simulation also concerned event-attendance writing, whereas the automated records identified entry and exit verification. The task content and timing boundaries therefore differ, so these measurements do not establish a directly comparable complete-task speed reduction.

Also add the missing explicit sentence in the manual procedure on page 12: **"Personnel did not check an ID or enrollment status during the timed paper-attendance simulation."**

### Pages 17-19: Administrator demographic denominator

The role distribution totals five people, but the years-of-service counts total only four: two in each of the reported categories. Do not silently change these to five or invent a fifth answer.

- Check the original demographic forms for a missed tally.
- If only four people answered, identify **valid n = 4 and one missing response**. The 50% figures can then refer to valid responses.
- Alternatively report percentages of all five: 40%, 40%, and 20% missing, if the forms confirm one nonresponse.
- Change "half of the respondents" in the discussion to "half of the valid responses" if using a four-response denominator.
- Review the overlapping 11-15 and 15-20 year intervals. Follow the original response choices and clarify the boundary; do not reassign responses without checking their forms.

### Pages 20-22 and 27: SD method and note

Replace the statement that all SDs use N with **sample SD uses N - 1**. The displayed item SDs and corrected ranges already follow the sample convention. Match the Chapter 3 Statistical Treatment section to that convention.

Replace the global claim that composite SD cannot be reconstructed with a distinction:

> Individual-item sample SDs were calculated using N - 1. Category summary ranges describe the minimum and maximum individual-item sample SDs, not a category-composite SD. Individual answers were available for the manual and student-system groups, permitting calculation of respondent-level category and overall scores and their sample SDs. Only item-frequency totals were available for security personnel and administrators, so respondent-level composite SDs were not reported for those groups.

Use **"Items"**, not "Item," and **"Item Sample SD Range"** in the category-summary headers.

### Page 22: Student interpretation paragraphs

The summary table is corrected, but the paragraphs still contain older results. Replace them with:

> Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.74. Functional Suitability received the highest category mean of 3.76, followed by Security at 3.75. Performance Efficiency and Usability each recorded 3.74, while Reliability recorded 3.71. At item level, the identity-confirmation statement received 3.84, while the lowest item mean was 3.60 for processing without unexpected failures or freezing. These ratings describe perceived quality and do not independently establish resistance to credential copying or impersonation.

### Pages 32-33: Administrator category summary

- Performance Efficiency has **5 items**, not 4.
- Compatibility has **4 items**, not 5.
- Overall equal-category mean is **3.81**, not 3.63.
- Keep the total at 39 items.

The eight category means average to 3.81375: (3.80 + 3.68 + 3.75 + 3.88 + 3.76 + 3.84 + 3.88 + 3.92) / 8. The narrative's 3.81 is already correct.

### Page 40: Administrator and manual headings

- Change the administrator all-category mean from the displayed **3.8 to 3.81** for accurate two-decimal reporting from all eight categories.
- Replace the truncated manual heading **"(n=7" with "(n = 70)."**
- For the manual group, rename "OVERALL SYSTEM WEIGHTED MEAN" on page 46 to **"Overall Manual-Process Mean."**

### Page 48: t-test caption

The t-test values and interpretation are correct. Add a unique numbered table caption, such as **"Welch Independent-Samples t-Test of Overall Survey Ratings."** Do not reuse another table's number.

## 3. Correct Composite SDs for the Existing Summary Rows

Keep individual-item SDs as they are when they match the corrected response tallies. Replace only falsely labelled category/overall SDs with the respondent-level values below.

| Category | Student-System Sample SD | Manual Sample SD |
| --- | ---: | ---: |
| Functional Suitability | 0.318 | 0.899 |
| Performance Efficiency | 0.366 | 0.919 |
| Usability | 0.329 | 0.854 |
| Reliability | 0.348 | 0.852 |
| Security | 0.341 | 0.883 |
| Overall equal-category score | 0.290 | 0.753 |

These are already correctly displayed in the comparison table on page 46. Use them consistently in category-footers on pages 23-26 and 42-46, or omit the footer SD column to avoid duplication. Keep the same chosen rounding precision throughout.

For security personnel and administrators, preserve the category mean but use a dash in the composite-SD cell with a note that individual response records are unavailable. Do not replace those cells with the student/manual SDs or an average of role-specific item SDs.

## 4. Structural Revisions

### Section numbering

- Page 2 jumps from 4.1 directly to 4.3.2. Restore the missing parent/method sections or renumber the retained structure consecutively. Do not retain section numbers whose parent sections are absent.
- The introduction claims functional-testing results, but a functionality-test-results section is not present in this PDF. Include the documented test outcomes or give a precise appendix cross-reference if they were intentionally moved.
- Pages 20-21 both use 4.5.1. Use 4.5.1 Scoring and Interpretation, 4.5.2 Student Evaluation, 4.5.3 Security Personnel Evaluation, 4.5.4 Administrator Evaluation, 4.5.5 Manual Pre-Evaluation, and 4.5.6 Manual/System Comparison.
- Page 49 should say **4.6.1 Interpretation of System Performance**, not a second 4.6 heading.
- There are two 4.6.2 subsections and repeated 4.6.3/4.6.4 sections. Merge related discussion instead of retaining two competing discussion sequences.
- Move **4.7 Summary** from pages 51-52 to the very end of Chapter 4, after all retained related-study discussion and limitations.

### Suggested discussion sequence

1. 4.6.1 Interpretation of System Performance
2. 4.6.2 Interpretation of Manual and Automated Measurements
3. 4.6.3 Interpretation of Security Outcomes
4. 4.6.4 Practical Advantages and Their Limits
5. 4.6.5 Methodological Limitations
6. 4.6.6 Comparison with Related Studies
7. 4.7 Summary

Merge useful portions of Vulnerability to Common Risks into the security/limitations discussion. Merge useful portions of the later Strengths/Implications subsections into Practical Advantages and Their Limits. Remove duplicate wording and all obsolete numerical claims.

### Table and figure numbering

Renumber all captions in order after the content is finalized. The current file reuses Tables 7, 8, 9, 10, 11, and 12 for unrelated objects. Figure 20 refers to both the system-timing figure on page 6 and the survey-comparison figure on page 47. Every table and figure must have one unique number, with corresponding narrative references updated.

### Layout and consistent precision

- Do not split a category-summary row across pages, as occurs for the overall student row on pages 21-22.
- Repeat table headers on continued pages and prevent individual questionnaire rows from splitting where practical.
- Keep a section heading with its first paragraph or table; the administrator item heading/table header at the bottom of page 33 is separated from its first question.
- Replace truncated headings, such as the manual n = 70 label on page 40.
- Remove the visible red spell-check marks under "Note" in the table images.
- Report survey means consistently to two decimals in item/category tables and three decimals where used for the inferential comparison.
- Use the exact survey wording actually administered. Do not retroactively alter a question merely to improve a technical description; clarify its interpretation outside the item text when necessary.

## 5. Add the Missing Explicit Study Limitations

Place a short subsection before Related Studies and the final Summary. It should identify:

- Manual and student-system respondents were different groups.
- The demographic groups differed substantially: the manual group was entirely Computer Science and comprised second-/fourth-year students, whereas the system group included Computer Science/Information System and first-/third-/fourth-year students. Thus the difference in ratings cannot be attributed solely to the method.
- The questionnaires used process-specific wording. The t-test concerns the defined composite ratings, not demonstrated measurement equivalence between instruments.
- Automated exports did not supply independently measured complete-task boundaries matched to the manual writing task. No processing-time t-test, complete-task percentage reduction, or fair throughput multiplier is established.
- Security-personnel and administrative sample sizes were five each. Technical-review exposure and raw individual response patterns were not documented.
- System observations used fixed mode order, 20 reused credential profiles, researcher-labelled expected conditions, and interspersed demonstrations. These limit generalization to ordinary campus entry traffic.

Do not remove these limitations merely to simplify the chapter. They explain what the reported evidence can and cannot support.

## 6. Keep These Results

- Pages 2-16: most of the current observation-method and component-performance content, after the listed numeric/label corrections.
- Manual writing baseline: 70 observations; total 1,526.24 s; mean 21.803 s; median 21.135 s; sample SD 4.436 s; range 13.21-37.63 s.
- Corrected manual category and item means on pages 40-46.
- Corrected comparison table on page 46 and updated chart on page 47.
- Correct t-test on page 48: t(89.08) = 15.12, p < .001; system-minus-manual difference 1.458; 95% CI [1.267, 1.650].
- The main discussion on pages 49-51, with numbering repaired and limitations expanded.
- The main summary on pages 51-52, moved after the final discussion.

This is a document review, not a re-verification of hardware behavior, original demographic forms, or external cited publications. The demographic missing-response issue and external-source claims require their respective original records.
