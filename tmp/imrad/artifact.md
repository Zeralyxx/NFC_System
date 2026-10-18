# IMRAD template execution contract

## Reference and evidence
- Reference: C:\Users\justine mendoza\Documents\METRICS first batch\IMRAD-TEMPLATE-CSIS (2).docx
- SHA-256: d2d0142c9d46aa4fa48ed461ad833baacd34cdc217cb454dc2b1876c525941ff
- Reference render: tmp/imrad/reference-render/reference.pdf, page-1.png and page-2.png. Both pages inspected.
- The canonical renderer was attempted but cannot find bundled LibreOffice on this Windows host. Microsoft Word exported the read-only reference successfully. Final layout will be checked using the same native Word export and bundled Poppler rasterization.
- Six sections. All are US Letter portrait, 8.5 by 11 inches, with left margin 1.5 inches and other margins 1 inch. Header/footer distance 706 twips. Column gap 708 twips. Section column counts are 1, 2, 2, 2, 1, 1. All subsequent sections are continuous.

## Components and typography
- Body/document.xml paragraph 0 contains the two university/college logos and the university-name text box, including AlternateContent fallback. Preserve this entire element and both media parts unchanged.
- No populated headers, footers, page-number fields, tables, footnotes, comments or content controls exist in the source. Do not introduce decorative furniture.
- Title slot is body paragraph 2: centered Arial 14 pt, bold, black. Add a matching Title paragraph style without changing its source appearance. The full thesis title replaces the slot and may wrap.
- Author slot is paragraph 5, followed by institution, college and city paragraphs 6-8: centered Arial 12 pt, normal weight, black.
- Email paragraphs 10-11 contain example mailto hyperlinks. Replace with three clearly marked author email placeholders and remove the example mailto relationships. User explicitly authorized placeholders.
- Abstract paragraph 14: full-width Arial 12 pt, bold Abstract label and regular body. Use 150-250 words. Keyword paragraph 16: italic, bold Key Words label, five terms.
- Section headings use the source Arial 12 pt bold role, black. Preserve the Roman numbered I-VII section names and order from the template.
- Body role comes from paragraph 21: BodyText style, direct Arial 12 pt; justified; 187 twips before; 220 twips right indent; source tabs give a 0.5 inch first-line indent. Clone the role, replacing the tab with an explicit 720 twip first-line indent. Set widow control to avoid isolated lines. Do not reduce body font.
- Subheadings use the source body role with bold Arial 12 pt, left alignment, no first-line indent, and keep with next.
- References use the source body role in two columns, no first-line indent, a 0.25 inch hanging indent, 12 pt Arial, URLs with zero-width soft wrap opportunities only if rendering needs them.
- Added result tables are permitted repeated numeric components within a column. Use width 3,750 twips, Arial 9.5 pt, light gray D9D9D9 borders, pale gray header fill, black text, dynamic row heights, repeating header, centered numeric values and left-aligned descriptors. Captions above, 10 pt Arial, bold, keep with next. No shaded narrative boxes.

## Content and slot map
- Rewrite title, author and email slots, abstract, keywords and all placeholder instructional body text.
- Retain affiliation and city wording. Retain the university branding.
- I Introduction: local problem, concise verified literature, precise study objective and scope.
- II Methods: developmental/descriptive evaluation, sampling, hardware/software, three modes, observation protocol, manual timing, instruments and statistical analysis.
- III Results: functionality cases, component timings, credential/state outcomes, manual baseline, role-specific surveys and Welch overall-score result. Four compact tables are allowed.
- IV Discussion: explain survey findings, security scope, component timing, workflow benefit and limitations. Avoid measured speed/throughput or universal security claims.
- V Conclusion: concise bounded conclusion.
- VI Recommendation: feasible measurement, security, recovery, usability and deployment follow-up.
- VII References: APA entries for only cited literature, verified against primary papers or official ISO material.
- Preserve all six source section-property elements, in the same order. Paragraph 18 ends the full-width abstract/title section; paragraph 20 is a same-column continuous break before body text; paragraph 32 ends Results; paragraph 43 ends References; paragraph 44 and the final body sectPr are retained terminal single-column elements.
- Empty filler paragraphs may be removed except those carrying source section properties. Expand each prose slot by cloning the source role. Never copy the sample authors or emails into the output.

## Preservation and fidelity gates
- All ZIP members are preserve-only except word/document.xml, word/styles.xml (matching Title role), word/_rels/document.xml.rels (remove example email links), word/settings.xml (field refresh), and docProps/core.xml (actual authors/title).
- Record package member sizes and SHA-256 in template-package-inventory.json before creating the result, then compare every preserve-only part byte-for-byte.
- Source file must retain its original SHA-256.
- Final section dimensions and ordered column settings must equal the source.
- Inspect every final page rendered by Word and Poppler. Verify title, logos, tables, formulas, column transitions, reference wrapping and no blank trailing pages or clipped text.
- Table numbers/captions are static, not Word fields. There is no TOC to refresh. Formula is native OMML and must be checked after Word export.
