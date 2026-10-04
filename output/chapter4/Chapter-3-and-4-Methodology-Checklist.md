# Chapter 3 and Chapter 4 Methodology Checklist

## Status

Chapter 4 includes the available implementation, performance, security, manual-baseline, and survey evidence. Sections 4.4 and 4.5 now contain the respondent role profile and all four survey instruments from `UnStudent Weighted Mean - Students (1).pdf`. Complete-task timing and independent error or fraud rate comparisons remain unavailable.

## Chapter 3 Clarifications

1. **Participants and records.** State that 70 participants completed each of three modes, producing 210 successful entry trials, not 210 distinct participants. Twenty preconfigured cards were reused. Team-assisted exit records reset the credential profiles and are not participant entry trials. Do not assume the 70 manual observations came from the same people until confirmed.
2. **Actual observation procedure.** Describe batches of 40 and 30 participants, rounds of 20/20 and 20/10, and fixed Fast-to-Standard-to-High-Security order. Separate intentional invalid-condition demonstrations from normal successful trials. Mention possible learning and order effects.
3. **Average processing time.** Retain the original NFC-tap-to-completed-outcome definition only if that full interval is measured. Describe exported internal latency and PIN/QR interaction as component measurements. Database time overlaps internal latency and must not be added again. Do not substitute component totals for an unmeasured complete interval.
4. **System throughput.** Retain successful completions divided by a defined complete observation period as the primary planned measure. Label first-to-last within-round entry timestamp spacing and its reciprocal rate as supplementary log-derived measures. There are 66 observed intervals per mode, not 70 complete service intervals.
5. **Authentication accuracy.** Define a correct decision as a correct acceptance of an expected-valid credential or a correct rejection of an expected-invalid credential. Document expected conditions independently of the recorded verdict. Report the researcher's retrospective condition confirmation and its limitations. Define the unit as an applicable recorded credential decision, not a unique student or NFC session. Exclude reset exits and pre-authentication state-rule violations from this credential-accuracy measure.
6. **Transaction denial rate.** Define the record-based denominator explicitly. Here it includes all 438 logged records, including 210 reset exits and intentional tests. Do not describe this as an ordinary student failure percentage.
7. **PIN lockout rate.** Count newly triggered lockouts, not repeat attempts against an already locked credential. Count all individual PIN submissions, including wrong-PIN retries, the triggering third submission, and accepted PINs followed by QR denial. The researcher confirmed that every PIN submission was captured and both lockouts were newly triggered.
8. **Manual baseline.** Describe the actual paper-event-attendance simulation: name, course and section, student ID, and signature; first written character to completion of all fields. No ID or enrollment check was performed. Call the result active writing duration, not complete identity-verification time. The reciprocal of mean writing time is a service-time-equivalent rate, not observed queue throughput.
9. **Statistical analysis.** Distinguish the sample SD reported for observational timings from the population-form item SD (denominator N) stated in Chapter 3 for survey ratings. Calculate item means from frequency totals, category means from unrounded item means, and group overall means with equal category weights. Do not average item SDs and call the result an overall respondent SD. Keep all successful timing observations, including slow trials. The aggregate survey tables support descriptive comparisons but contain no linked participant responses for a paired analysis.

These changes must describe what actually happened. They should be reviewed with the adviser, not presented as though missing planned measurements had been collected.

## Verified Calculations

| Measure | Calculation | Interpretation |
|---|---|---|
| Observed credential-decision accuracy | 222 / 222 = 100.00% | Researcher-labelled recorded valid grants and deliberate credential denials; not universal identity accuracy |
| Recorded transaction denial rate | 18 / 438 = 4.11% | Mixed observation and demonstration records, including reset exits |
| Recorded PIN lockout rate | 2 / 152 = 1.32% | Two intentionally triggered lockouts; every individual submission captured |
| Manual mean writing duration | 1,526.24 s / 70 = 21.803 s | Active writing stage only |
| Manual sample SD | 4.436 s | Variation among the 70 supplied durations |
| Manual service-time-equivalent rate | 60 / 21.803 = 2.75 per minute | Hypothetical consecutive writing service without additional gaps |

The 152 PIN submissions comprise 140 successful-entry PIN stages, eight PIN failures, two triggering lockout submissions, and two accepted PIN stages followed by QR mismatch.

## Evidence Still Required

- Complete NFC-tap-to-final-outcome durations if the original automated processing-time definition is to be fulfilled.
- Defined full-round start and finish measurements for directly observed throughput, including an explicit policy on pauses, resets, and invalid attempts.
- A manual and automated comparison using the same task outcome and defensible timing boundaries. The supplied automated exports describe entry/exit verification, while the manual simulation describes event-attendance writing.
- Confirmation of whether the manual pre-evaluation was administered before system exposure and whether the two student survey groups contained the same people.
- An independent paper-record audit or challenge-test evidence before calculating transcription-error, proxy-attendance, or real-enrollment false-acceptance rates.
- Respondent-level cross-item data only if category-composite or overall respondent-score SDs, correlations, or paired statistical tests are required. The supplied item frequencies already support item means and item SDs.
- Any demographic details beyond role distribution that must be reported; age, sex, course, and year-level frequency tables were not provided.
- The extent of technical review undertaken by the five administrative respondents, particularly for the source-code maintainability indicators.
- Confirmation of manual test dates and whether manual and automated participants were the same cohort.

The absence of an enrollment check in the manual procedure establishes a procedural omission. It does not establish that any participant was actually unenrolled or fraudulent.

## Submission Check

- Replace interface illustrations if they no longer represent the final application build.
- Keep functionality-test PASS statements attributed to the documented test record; no new live tests were performed for the chapter.
- Present the system observation directly, without describing it as revised or reconducted.
- Match the final thesis template's section numbering, fonts, captions, and reference conventions.
- Preserve raw exports and manual observations alongside the calculation workbook.
- Do not restore the former assumed 2.50-second baseline or manual-versus-system improvement multipliers.

## Survey Calculation Notes

- The corrected source contains 111 item tallies: 24 student items, 24 security-personnel items, 39 faculty/event-organizer/admin items, and 24 manual pre-evaluation items. All item counts match their respective respondent totals.
- Equal-category overall means are 3.76, 3.63, 3.81, and 2.37, respectively.
- The administrative overall mean is 3.81375 from the eight category means, displayed as 3.81 in the chapter. The source sheet displays 3.8; the chapter consistently reports two decimal places.
- Student Usability is 3.754285714 from the frequency totals, displayed as 3.75. Averaging rounded item means produces the source sheet's 3.76; calculate before rounding.
- The source uses sample-form item SDs. The chapter's survey tables use denominator N to match the stated Chapter 3 formula. Both conventions are shown in the separate calculation audit.
- Category or overall respondent-score SD cannot be recovered by averaging item SDs. The chapter reports individual-item SDs and clearly labelled ranges of item SDs by category.
- The student system and manual overall means differ descriptively by 1.39 scale points. This is not a processing-time improvement percentage or a demonstrated causal effect.
