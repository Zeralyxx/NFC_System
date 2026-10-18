# Verification of the Latest Full Thesis

Source: `C:\Users\justine mendoza\Documents\METRICS first batch\Final Draft Chapter 1-5 Thesis (3).pdf`.

The current PDF contains 237 pages. Chapter 4 occupies printed pages 106-167; Chapter 5 occupies pages 168-186. This check covers numerical consistency and the agreement of methods, results, and conclusions. It does not independently reproduce the experiments, certify the implementation, verify every literature citation, or establish legal compliance. The PDF was not edited.

## Decision

The main Chapter 4 and Chapter 5 findings agree with the supplied calculation records. The latest Chapter 5 no longer uses the obsolete speed, accuracy, denial, lockout, or pooled-survey claims. However, the thesis is not yet fully consistent: Chapter 3 still promises a processing-time t-test, uses a conflicting population-SD formula, and describes unmeasured full-task performance as measured. A demographic-table mismatch and stale contents page also remain.

Because the user's request makes the abstract conditional on everything being in order, this verification does not finalize an abstract. The fixes below should be applied before that final step. They do not require changing the settled raw responses or numerical results.

## Confirmed Results to Retain

| Finding | Confirmed Reporting |
| --- | --- |
| System observation | 438 records: 210 successful entries, 210 granted reset exits, 18 denials |
| Participant/mode structure | Same 70 participants across three modes; 70 successful entries per mode; 20 reused credential profiles |
| Mean internal latency | Fast 11.49 ms; Standard 57.37 ms; High-Security 54.06 ms |
| Recorded interaction | Standard PIN 2.505 s; High-Security PIN 2.162 s + QR 3.637 s = 5.799 s |
| Recorded-entry rate | 21.98 / 7.46 / 4.95 intervals/min; supplementary timestamp-spacing measures |
| Applicable recorded credential decisions | 222/222 correct = 100.00%, with stated controlled-test limits |
| Denial proportion | 18/438 = 4.11% |
| Deliberately triggered PIN lockouts | 2/152 submissions = 1.32% |
| Manual active writing | n = 70; mean 21.803 s; sample SD 4.436 s |
| Overall system-group survey means | Students 3.74; security personnel 3.63; faculty/event-organizer/administrator group 3.81 |
| Overall manual survey mean | 2.28, or 2.284 at three-decimal precision |
| Student Performance Efficiency footer | Corrected to sample SD 0.366 |
| Overall survey comparison | Manual 2.284, SD 0.753; system 3.742, SD 0.290; independent groups of 70 |
| Welch test | t = 15.124; df = 89.076; p < .001; difference 1.458; 95% CI [1.267, 1.650] |
| Chapter 5 tables | Tables 44 and 45 follow Chapter 4's Table 43; role-specific means are not pooled |

The survey inference concerns reported overall ratings, not complete-task performance or causal improvement. Keep that distinction in the final abstract.

## 1. Chapter 3 Respondent Scope and Generalization: Pages 45-48

The sampling description initially discusses only 80 respondents, while the data-gathering section on page 53 includes the additional 70-person manual group. The student paragraph on page 46 also calls a convenience sample from CICT a highly valid representation of the general student body. Uniform hardware interaction alone does not establish representativeness.

Replace the final sampling paragraph on page 45 and the sample-size paragraph on page 46 with:

> The study used purposive and convenience sampling to recruit respondents relevant to the prototype's student-verification, security-monitoring, and event-administration workflows. The proposed-system evaluation included 80 respondents: 70 students from the College of Information and Communication Technology, five security personnel, and five faculty/event-organizer/administrator respondents. A separate group of 70 students evaluated the manual process. The student survey groups were independent and were not matched pairs. The non-probability sampling design supported evaluation within the selected groups but did not establish representativeness of the entire university population.

Replace the opening of Description of Respondents with:

> The proposed-system survey respondents were divided into three groups according to their intended interaction with the prototype. A separate manual-process student group supplied the baseline survey responses. The groups were described separately because their roles, instruments, and evaluation experiences differed.

Replace the Students description on page 46 with:

> Students (70 proposed-system respondents). These respondents came from the College of Information and Communication Technology and evaluated the student-facing verification experience, including NFC presentation, PIN entry, QR interaction where required, and the relevant quality indicators. Their selection reflected access to respondents for controlled prototype evaluation. The findings describe this participating group and should not be generalized to all university students solely because the physical credential steps are similar across academic departments.

Retain the five-person role-group descriptions and their counts. Add after them:

> Manual-process students (70 respondents). A separate student group evaluated the paper-based process using the corresponding five-category questionnaire. These respondents were different from the students in the proposed-system survey group.

Table 1 can retain its 5/5/70 breakdown if its caption is changed to **Distribution of Proposed-System Survey Respondents (n = 80)**. The accompanying text must explicitly account for the separate manual group rather than presenting that table as the distribution of every study respondent.

## 2. Manual Timing Description: Pages 55-56

The current methods call the measurement total elapsed processing time and mention video recordings. The verified manual measure was active writing from the first character to completion of the required fields. Do not imply that waiting, identity checks, or other complete-task stages were measured.

Replace item 1, Manual Process Evaluation, on page 55 with:

> Manual Process Evaluation. A separate group of 70 student respondents evaluated the manual paper-based attendance process. The manual observation records contained 70 writing durations for supplying name, course and section, student ID, and signature. Timing began with the first written character and ended when all required information had been completed. The measure represented active attendance-writing duration, not queue waiting, complete identity verification, or later data encoding. Personnel did not check identification cards or enrollment status during the timed simulation. The manual questionnaire supplied a separate assessment of perceived process quality.

Do not assert that the manual timing observers and survey respondents were exactly the same individuals unless the records establish that link. The present findings establish the separate survey groups and the count of writing observations.

Replace item 5, Performance Data Recording, on page 56 with:

> Performance Data Recording. Automated performance data were obtained from system exports containing internal processing and database-query timing fields, recorded PIN/QR interaction fields, transaction timestamps, and verdict codes. These exports did not independently time the complete NFC-presentation-to-finished-verification task. The manual baseline used the supplied individual attendance-writing durations with the stated first-character-to-completion boundary. The available measurements were retained as distinct components rather than combined into an assumed complete-task duration.

Mention video recordings only if they were actually used and retained. They are not established by the supplied data or the prior clarification that automated timings came only from system exports.

## 3. Processing-Time T-Test Promise: Page 56

Item 7 currently promises an independent-samples t-test of mean evaluation scores AND transaction speeds. This directly contradicts page 61 and Chapters 4-5.

Replace the entire Comparative Analysis item with:

> Comparative Analysis. The manual and proposed-system student survey groups were treated as independent samples. A two-tailed Welch independent-samples t-test at a significance level of .05 compared respondent-level overall survey scores, calculated by giving equal weight to the five corresponding evaluation categories. The observation-based timing components and rates were summarized descriptively. No processing-time t-test was performed because the available manual and automated measurements did not cover equivalent complete-task boundaries. Differences among the three automated modes were also reported descriptively, with the shared participants and fixed mode order acknowledged.

## 4. Conflicting SD Formula: Page 61

The sample-SD formula on page 60 correctly uses n - 1, but the separate Standard Deviation formula on page 61 still uses N. Replace that separate paragraph, formula, and definitions with:

> Sample standard deviation was calculated to describe dispersion in individual observations or respondent scores. The calculation used n - 1 in the denominator. Individual-item SDs were calculated from item responses, while category and overall composite SDs were calculated from each respondent's category and overall scores where individual records were available. An average of item SDs was not treated as a composite SD. Composite SDs for security personnel and faculty/event-organizer/administrator respondents were not reported because only item-frequency totals were supplied for those groups.

Formula, in plain notation:

`s = sqrt[ sum((x_i - x_bar)^2) / (n - 1) ]`

Where `s` is the sample standard deviation, `x_i` is an individual observed value or respondent score, `x_bar` is the sample mean, and `n` is the number of observations or respondents.

The correct sample-SD formula already shown on page 60 may be reused; the point is to remove the conflicting population denominator, not add a different convention.

## 5. Claim That Complete Automated Processing Time Was Measured: Page 61

Replace the Average Processing Time description with:

> Average Processing Time. The intended complete-task measure was the duration from NFC presentation to finished verification. Independent start and finish timestamps covering that boundary were not available in the supplied automated exports. The analysis therefore calculated means separately for the available internal system-latency and recorded PIN/QR interaction fields. Manual active writing duration was summarized using its distinct first-character-to-completion boundary. These component means were not presented as equivalent complete-task durations.

A generic arithmetic-mean formula may remain:

`Mean duration = sum of observed durations / number of applicable observations`

Identify the specific component and unit each time it is used. Do not add overlapping database-query time to internal latency. Fast's bypassed PIN/QR stage is not a zero-second complete transaction.

## 6. Claim That Peak Throughput Was Measured: Page 62

Replace the entire System Throughput Rate discussion, including the claim that processing times differ significantly, with:

> System Throughput Rate. Complete-round throughput is conceptually defined as the number of completed successful tasks divided by an independently recorded elapsed observation period in minutes. Such complete-round start and finish measurements were not supplied for the automated observation. Therefore, the analysis did not report measured peak campus capacity or a matched overall-throughput advantage over the manual process. Supplementary recorded-entry spacing and rate were calculated from successful-entry timestamps within observation groups, as described in the following supplementary analysis. The manual service-time-equivalent rate was derived separately from mean active writing duration. These rates used different measurement boundaries and were not treated as directly comparable overall throughput.

The conceptual formula can remain if clearly labelled:

`Complete-round throughput = completed successful tasks / independently measured elapsed minutes`

Keep the accurate supplementary spacing and rate method on pages 63-64. The automated rate uses summed n - 1 intervals, not the number of complete individually timed transactions. No mode-level significance test was conducted, so remove the statistical-sounding assertion that mode times differ significantly.

## 7. Accuracy, Denial, and Lockout Definitions: Pages 62-63

These definitions must agree with the actual denominators used in Chapters 4-5.

### Authentication Accuracy Rate

Replace its description and formula with:

> Observed credential-decision accuracy measured agreement between the researcher-confirmed expected outcome and the recorded actual outcome for applicable credential decisions. Both an expected-valid grant and an expected-invalid credential rejection counted as correct decisions. Team reset exits and pre-authentication entry-state or exit-state violations were excluded from the credential-accuracy denominator and reported separately. The measure described the recorded controlled test conditions rather than exclusive ownership of each credential or universal authentication accuracy.

`Observed credential-decision accuracy (%) = correct applicable recorded credential decisions / all applicable recorded credential decisions x 100`

### Recorded Transaction Denial Proportion

Retain the explanation that these are configured denials, but replace the formula's ambiguous Failed Transactions wording with:

`Recorded denial proportion (%) = denied exported records / all exported transaction records x 100`

Add:

> The record denominator included participant entries, granted team reset exits, and recorded invalid-condition challenges. The proportion was not a participant-only failure rate, and a correct configured denial did not indicate a system malfunction.

### Recorded PIN Lockout Rate

Replace the claim that the rate directly reflects PIN-interface usability with:

> Recorded PIN lockout rate measured newly triggered lockouts divided by captured individual PIN submissions, including unsuccessful retries and the submission that triggered a lockout. Repeated use of an already locked credential was not counted as a newly triggered event. Correct PIN stages followed by QR denial remained applicable PIN submissions, while Fast-mode transactions and team reset exits were excluded. Deliberately triggered demonstrations were identified separately from ordinary-user usability failures.

`Recorded PIN lockout rate (%) = newly triggered lockout events / captured individual PIN submissions x 100`

## 8. Descriptive Comparison Scope: Page 63

Replace Comparative Analysis (Descriptive) with:

> The descriptive manual/system comparison addressed the available processing measurements, supplementary rates, and mean survey ratings specified in SOP 6. Manual writing time and automated internal or interaction-stage timing were reported with their distinct boundaries, without calculating an unsupported complete-task percentage speed improvement. The survey categories were compared descriptively, and the overall respondent-level survey score was compared using the Welch test specified above. Mode comparisons acknowledged repeated participation and the fixed Fast-to-Standard-to-High-Security order. Configured validation controls were discussed separately from unmeasured reductions in transcription errors, proxy attendance, or enrollment violations.

## 9. Demographic Table Still Does Not Match Its Note: Page 123

The service table still displays 11-15 and 15-20 years with two responses each. The note says the groups have been combined into 11-20 years. The two displayed categories also overlap at 15.

Replace only the service rows with:

| Years of Service (Valid n = 4) | Frequency | Percentage of Valid Responses |
| --- | ---: | ---: |
| 0-5 years | 0 | 0.00% |
| 6-10 years | 0 | 0.00% |
| 11-20 years | 4 | 100.00% |
| **Valid responses** | **4** | **100.00%** |

Keep the note explaining that the student event organizer did not supply years of service and that the four valid responses form the service denominator. Keep the five-person role distribution unchanged.

If separate original service categories are verified from the actual form, they may be retained instead, but the combining sentence must then be removed and any overlap handled transparently. Do not invent a new service answer or a different unverified interval.

## 10. Contents and Small Presentation Fixes

- Pages 11-12: regenerate the table of contents. It still lists removed sections and old Chapter 4/5 locations. Current Chapter 4 starts on 106, Chapter 5 on 168, and BIBLIOGRAPHY on 187, before any additional edits or abstract insertion.
- Pages 175-176: repeat Table 45's column headers on the continuation page.
- Table 45: the replacement chapter's manual mean-writing row was omitted during integration. Restoring it would make the summary more complete: **Mean manual writing duration | 21.803 s; sample SD 4.436 s | Active attendance writing; 70 observations**. The figure is already reported correctly in the narrative, so this omission does not change the calculation.
- Page 176: replace the sentence fragment with **Table 45 summarizes the observation findings while preserving their measurement boundaries.**
- Page 182: remove the double period after **controls**.
- Page 182: change **Counterbalance mode in order to reduce practice effects** to **Counterbalance mode order to reduce practice effects**.
- Page 174: use **Overall, equal category weights** rather than the singular **weight** in Table 44.

Do not renumber Tables 44 and 45 merely because the earlier optional worked-input caption was not added. They correctly follow the current Chapter 4 Table 43.

## Next-Step Gate

The settled results can support an abstract describing developmental/descriptive-evaluative prototype research, non-probability respondent selection, operator-configured modes, controlled observation, separate respondent-group evaluations, and the significant overall survey-score difference. It must not claim measured complete-task superiority, automated individual risk scoring, universal security accuracy, formal ISO certification, or population representativeness.

The remaining method/result conflicts should be corrected first. After the latest copy incorporates those corrections, the abstract can be finalized without changing the verified data.

No original responses, observation exports, or supplied PDFs were modified in this verification.
