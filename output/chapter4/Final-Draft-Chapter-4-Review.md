# Chapter 4 Review: Latest Full Draft

Source: `C:\Users\justine mendoza\Documents\METRICS first batch\Final Draft Chapter 1-5 Thesis (1).pdf`.

Reviewed Chapter 4, printed pages 106-166, through complete text extraction, targeted visual inspection of the demographic table, student summary and Performance Efficiency footer, manual sample heading, survey figure, worked computation, and Welch results. Numerical checks used the previously supplied raw-response and observation calculation records. This is a reporting and numerical-consistency review, not independent verification of the experiment. The PDF was not edited.

## Verdict

Most earlier corrections are now applied. The prototype wording, corrected survey interpretation, personnel composite-SD dashes, administrative overall mean, manual composite SDs, consolidated discussion, and principal observation and Welch results are consistent with the available records. Chapter 4 is not quite ready: one student composite SD is wrong, the manual sample heading is wrong, a student SD footnote remains outdated, and several labels and methodology references need correction.

All page numbers below are those printed in this new PDF. They differ from the prior draft.

## 1. Incorrect Student Performance Efficiency Composite SD: Page 129

In Table 17, the row labelled **Category Mean and Composite Sample SD** currently displays:

| Mean | Composite Sample SD |
| ---: | ---: |
| 3.74 | 0.318 |

Replace only that footer SD:

| Mean | Composite Sample SD |
| ---: | ---: |
| **3.74** | **0.366** |

The value 0.318 belongs to student Functional Suitability, not Performance Efficiency. The respondent-level Performance Efficiency sample SD is 0.366068588..., reported as 0.366. The comparison table on page 151 already shows 0.366 correctly.

Leave the individual-question SDs unchanged. The other student footer values are correct:

| Current Page | Category | Composite Sample SD |
| --- | --- | ---: |
| 128 | Functional Suitability | 0.318 |
| 130 | Usability | 0.329 |
| 130 | Reliability | 0.348 |
| 131 | Security | 0.341 |
| 131 | Overall equal-category score | 0.290 |

## 2. Incorrect Manual Sample Size in Heading: Page 146

Current heading:

**4.5.5 Manual Process Student Pre-Evaluation (n=7)**

Replace with:

**4.5.5 Manual Process Student Pre-Evaluation (n = 70)**

The manual survey contains 70 respondents. Its descriptive comparison and t-test already use the correct n = 70; this is a heading error, not a reason to change the analysis.

## 3. Outdated Student SD Note: Page 127

The student category-summary note still says that item SDs use **N** in the denominator and discusses inability to recover composite SDs from frequency totals without explaining that individual student responses were available. This conflicts with the sample SDs actually reported and with the correct scoring explanation on page 126.

Replace the entire note beneath the student category summary with:

> The SD range gives the minimum and maximum individual-item sample standard deviations within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall scores give equal weight to the five categories. Individual student responses were available, allowing calculation of respondent-level category and overall sample SDs. These composite SDs are reported in the detailed-table summary rows and in Table 42, the manual/system survey comparison.

The security-personnel note on page 132 and administrative note on page 138 already use N - 1 and correctly explain the unavailable composite SDs. Keep those notes and their footer dashes.

## 4. SOP 6 Subsection Does Not Match the Current Research Question: Pages 120-121

The current SOP 6 on page 7 asks for comparison of:

- A. Average processing time per student.
- B. Overall system throughput rate.
- C. Mean or weighted mean ratings across Functional Suitability, Performance Efficiency, Usability, Reliability, and Security.

However, subsection **4.3.5.3 Human Error Proxy Attendance and Unverified Enrollment** still addresses the earlier error/fraud topic instead of the current item C. Its final paragraph also describes incomplete error/proxy-rate comparisons as if these were still the specified SOP 6 outcomes.

Replace the 4.3.5.3 heading and its associated paragraphs with:

### 4.3.5.3 Mean or Weighted Mean Survey Ratings

The manual and proposed-system groups were compared across the five corresponding evaluation categories: Functional Suitability, Performance Efficiency, Usability, Reliability, and Security. The manual group obtained category means of 2.48, 1.93, 2.64, 2.18, and 2.18, respectively, while the proposed-system group obtained 3.76, 3.74, 3.74, 3.71, and 3.75. Their overall equal-category means were 2.284 and 3.742, respectively. Each group consisted of 70 different student respondents. Detailed descriptive results are presented in Section 4.5.6, and the Welch independent-samples t-test is presented in Section 4.6.

The observations support descriptive reporting of the available timing components and log-derived entry spacing. They do not establish a matched complete-task processing-time or overall-throughput advantage because the methods used different measurement boundaries. The survey results address SOP 6's rating comparison, subject to the limitations of different respondent groups and process-specific questionnaire wording.

Preserve the useful current discussion about the absence of manual ID/enrollment checks and the limits of error/fraud claims under a separate heading, for example **4.3.5.4 Supplementary Validation-Procedure Comparison**. If retaining that text, replace its concluding paragraph with:

> The manual pre-evaluation provides evidence of respondents' perceptions of reliability and security, but does not measure the incidence of transcription errors, proxy attendance, or false enrollment acceptance. Those outcomes would require an independent record audit or documented challenge attempts. The present findings therefore distinguish observed procedure differences and configured rule enforcement from unmeasured reductions in error or fraud.

Do not remove the existing disclosure that complete automated task duration was not independently measured. No wording change can supply that missing measurement.

## 5. Demographic Note and Table Disagree: Page 123

The missing-response explanation is now correct: the student event organizer did not provide years of service, so the valid service denominator is four. However, the note says that an 11-20-year group combines the two upper categories, while the table still displays separate 11-15 and 15-20 groups. These intervals also overlap at 15.

Recommended correction: keep the role distribution unchanged and replace only the service portion with:

| Years of Service (Valid n = 4) | Frequency | Percentage of Valid Responses |
| --- | ---: | ---: |
| 0-5 years | 0 | 0.00% |
| 6-10 years | 0 | 0.00% |
| 11-20 years | 4 | 100.00% |
| **Valid responses** | **4** | **100.00%** |

This matches the existing combined-range narrative on page 124. Keep the current note explaining the one missing response and the consolidation. It does not imply that the event organizer has zero years of service.

Alternatively, retain separate two-and-two groups only after checking the actual original, non-overlapping response categories. Do not invent a 16-20-year category. If separate verified groups are retained, remove the sentence claiming that the table combines them.

## 6. Duplicate Evaluation Subsection Number: Page 151

The manual pre-evaluation already uses 4.5.5 on page 146. The comparison heading also uses 4.5.5 on page 151.

Change:

**4.5.5 Comparison of Manual and Proposed-System Survey Ratings**

to:

**4.5.6 Comparison of Manual and Proposed-System Survey Ratings**

Keep the separate main section **4.6 Independent Samples t-Test**, **4.7 Discussion**, and **4.8 Summary**. That sequence is valid; it does not have to be changed back to the older proposed sequence.

## 7. Placeholder T-Test Caption: Pages 154-155

Table 42 on page 151 is currently the last numbered table before the worked t-test. The inputs table on page 154 has no caption, and the results table on page 155 is still labelled Table 999.

Use:

- Page 154: **Table 43. Summary Statistics Used in the Worked Welch t-Test Computation**.
- Page 155: **Table 44. Welch Independent-Samples t-Test of Overall Survey Ratings**.

Update any related in-text references. Do not change the numerical results:

| Mean Difference | t | Welch df | Two-Tailed p | 95% CI |
| ---: | ---: | ---: | --- | --- |
| 1.458 | 15.124 | 89.076 | < .001 | [1.267, 1.650] |

The worked formulas on page 154 render correctly. Its rounded-input SE of approximately 0.096445 and t of approximately 15.12 are consistent with the rounding explanation. Recalculation from the stored individual scores gives t = 15.1241767... and df = 89.0760179..., supporting the final values.

## 8. Table of Contents Is Still From the Older Draft: Pages 11-12

The contents list still places Chapter 4 on page 102 and contains removed headings such as Implementation Details, System Interfaces, Key Code Snippets, and Comparison with Existing Systems. The actual Chapter 4 begins on page 106 and now ends with 4.8 Summary.

Regenerate the table of contents from the actual headings after the corrections above. Update any table/figure lists and references after final pagination, rather than manually preserving outdated page numbers.

## 9. Layout and Decimal Consistency

- Page 127 starts with the end of an interpretation cell from the preceding page. Keep category-summary rows together and repeat the header on continued tables.
- Several detailed tables continue onto another page without repeating their column headings, including page 129. Use repeated headers for continued tables.
- Use consistent two-decimal mean display, for example 3.70, 3.80, 3.60, and 4.00 instead of 3.7, 3.8, 3.6, and 4. This is a display correction, not a data change.
- Keep three decimals for the composite SDs shown in the detailed footer and survey comparison tables.
- Correct the timing caption on page 111 to **Figure 19. Successful-Entry Timing by Verification Mode**. Standardize QR capitalization in Table 5 on page 110 and PIN capitalization in Table 10 on page 116.
- Add the missing final period after the Calo comparison paragraph on page 164.

## Current Numbering That Is Already Valid

The latest thesis ends Chapter 3's figure sequence at Figure 18. Chapter 4's Figures 19, 20, 21, and 22 therefore follow correctly. Do not force the survey chart to Figure 23 based on the previous draft's figure sequence.

Tables 3-42 now follow consecutively. The current security-personnel Security table is **Table 26**, which is correct in this new sequence. The earlier suggestion to use Table 27 assumed insertion of an extra Chapter 4 functionality-summary table; that extra table was not inserted. Appendix H is now explicitly cited in 4.1 and the discussion, so a separate summary table is optional, not a reason to renumber every existing table again.

## Verified Findings to Keep

- Observation totals: 438 records = 210 successful entry trials + 210 granted reset exits + 18 denials.
- Seventy successful entry trials per mode, using the same 70 participants across modes and 20 reused credential profiles.
- Internal latency: 11.49, 57.37, and 54.06 ms for Fast, Standard, and High-Security, with sample SDs 2.20, 17.46, and 6.34 ms.
- Interaction durations: Standard 2.505 seconds; High-Security PIN 2.162 seconds plus QR 3.637 seconds, totaling 5.799 seconds.
- Recorded-entry spacing: 66 within-round intervals per mode; supplementary rates 21.98, 7.46, and 4.95 intervals/minute.
- Observed applicable credential-decision accuracy: 222/222 = 100.00%, properly restricted to the known recorded test conditions.
- Denial proportion: 18/438 = 4.11%; deliberate-trigger PIN lockout rate: 2/152 = 1.32%.
- Manual writing duration: mean 21.803 seconds and sample SD 4.436 seconds, n = 70.
- Overall survey means: students 3.74, security personnel 3.63, faculty/event-organizer/administrator group 3.81, manual students 2.28.
- Student/manual overall composite sample SDs: 0.290 and 0.753.
- The merged discussion on pages 156-160 no longer repeats the two manual-comparison or two strengths subsections.
- The implications, related-study discussion, and summary correctly avoid claiming a matched objective speed improvement or universal security effectiveness.

The differences of 1.529 for Reliability and 1.569 for Security in Table 42 are calculated from unrounded scores. They need not equal subtraction of the displayed three-decimal means at the final digit.

## Cross-Chapter Conflicts to Address Next

These do not invalidate the corrected Chapter 4 calculations, but the full thesis remains inconsistent until they are updated.

### Chapter 3

- Page 56 still promises a t-test of transaction speeds, whereas page 61 and Chapter 4 explicitly restrict the test to overall survey ratings. Remove the processing-time t-test promise.
- Page 61 still has a separate SD formula using N, despite the correct sample-SD formula using n - 1 on page 60 and the Chapter 4 sample SDs. Make both use n - 1.
- Pages 61-62 still describe complete processing time and peak throughput as already measured. Align this with the unavailable complete-task boundary and supplementary interval-rate definitions in Chapter 4.
- Page 62 describes accuracy in terms of successful workflows, whereas Chapter 4 includes correct rejections as correct decisions. Define accuracy consistently as agreement between expected and actual applicable credential decisions.

### Chapter 5

- Page 168 still reports Fast 24.00/min, manual 2.56/min, an average DB time of 15.42 ms, and a 0.00% PIN lockout rate. These do not match the corrected Chapter 4 summaries or their measurement definitions.
- Pages 175-176 still use manual 23.47 seconds, Fast 2.50 seconds, Standard 8.65 seconds, and High-Security 14.18 seconds and claim higher comparable processing capacity. Chapter 4 explicitly explains why its available measurements do not establish that comparison.
- Chapter 5 still presents pooled ISO-category results, while Chapter 4 appropriately reports the different respondent groups separately because the role-specific instruments differ.

Do not restore old Chapter 5 numbers in Chapter 4 to make them agree. Update Chapter 5 to reflect the corrected evidence and limitations.

## Evidence Boundary

The available records support the descriptive component timing analysis and the independent-groups survey comparison. They do not supply matched full-task durations, random group assignment, independent identity verification for all physical participants, or a comprehensive attack evaluation. The current Chapter 4 limitations largely handle those boundaries correctly. The review confirms consistency with the supplied records and explanations, not independent reproduction of the experiment or formal software certification.
