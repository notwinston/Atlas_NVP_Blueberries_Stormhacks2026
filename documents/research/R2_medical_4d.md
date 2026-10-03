# R2 — The "4D" of the Lung blade: what it is, why it is useful, how to build it

Research memo for the ATLAS lead engineer · 3 Oct 2026 · scope: medical (lung) 4D only.
Framing throughout: **education, training and research visualisation on public data. No clinical claims.**

**Evidence tags** (one per factual claim):

| Tag | Meaning |
|---|---|
| **[V]** | verified: fetched primary source today (paper abstract or full text, official page, source code, or the TCIA API and DICOM files I queried myself) |
| **[S]** | secondary (review, search snippet, or a standard result recalled but not fetched today) |
| **[D]** | my derivation (maths or a computation I ran; scripts are in `research/R2_assets/`) |
| **[E]** | estimate (engineering judgement; must be tested on the device) |

---

## 0. Bottom line

1. **The medical 4D is 4D-CT: three space axes plus respiratory phase, the product space ℝ³ × S¹.** Clinicians already call this "4D" [V: Vedam 2003, TG-76]. Over one breath the tumour traces a closed tube in that space. The challenge's three verbs map onto three real clinical objects: **slice** = the anatomy at one breathing phase; **shadow** = the internal target volume (union over phases) and the maximum-intensity projection; **rotation in a space–time plane** = the tilted surface a moving-table CT actually samples, which is where motion artefacts come from.
2. **One slicer serves the tesseract and the tumour.** Both are tetrahedral meshes in ℝ⁴ cut by a hyperplane. The tesseract toybox is therefore the Phase 1 deliverable *and* the unit test for the Phase 2 medical view. I prototyped the slicer in Python and it reproduces the known tesseract cross-sections exactly [D].
3. **Real data is usable tonight with no agreement.** TCIA 4D-Lung is CC BY 3.0. I queried the archive directly: tumour contours exist on all 10 phases for 20 of 20 patients, but lungs exist on all 10 phases for only 5 of 20. **Patient `101_HM10395` is the best single case** (tumour, lungs, heart, oesophagus, cord, trachea on every phase). One 10-phase study is 0.8 GB and downloads in minutes [V].
4. **The real tumours barely move.** Across the 20 patients the median superior–inferior centroid range is 4.3 mm and the largest is 11.3 mm [D from the RTSTRUCTs]. A real worldtube is a nearly straight sausage. **Pair the real case with a clearly labelled illustrative small mobile nodule** (10 mm nodule, 15 mm motion), where the tube is a dramatic wave and no single point is inside the tumour at every phase.
5. **Slice on the CPU with Burst, live.** A 500–600-triangle tumour template gives 15–18k tetrahedra per breath. Estimated cost is under 1 ms, and zero when the slice is not moving [E]. Engine4D's GPU slicer is the fallback above about 50k tetrahedra.
6. **Two errors in the Spec need fixing** (section F): the Slice Tilt plane is misnamed, and "blend neighbouring precomputed slices" is impossible for meshes whose topology changes.

---

## A. What "4D" legitimately means for the lung

### A.1 The object

Phase-binned 4D-CT is a stack of 3D volumes indexed by respiratory phase φ ∈ {0 %, 10 %, …, 90 %}. Phase is cyclic, so the data is a function on ℝ³ × S¹:

```
I : ℝ³ × S¹ → HU                     the 4D image
G(φ) ⊂ ℝ³                            the tumour (GTV) at phase φ
W = { (x, φ) : x ∈ G(φ) } ⊂ ℝ³ × S¹   the tumour's "worldtube"
```

Everything the blade shows is one of these operations on W [D]:

| Operation | Formula | Clinical name |
|---|---|---|
| Slice at a phase | W ∩ {φ = φ₀} = G(φ₀) | one phase of the 4D-CT |
| Shadow along phase | π(W) = ⋃_φ G(φ) | IGTV / ITV (motion envelope) |
| Grey-level shadow | MIP(x) = max_φ I(x, φ);  AIP(x) = mean_φ I(x, φ) | maximum / average intensity projection |
| Dwell-time shadow | ρ(x) = (1/2π) ∫ 1_W(x, φ) dφ ∈ [0, 1] | occupancy (probability the tumour is at x) |
| Core | {ρ = 1} = ⋂_φ G(φ) | no standard name; the points always inside the tumour |
| Gated shadow | π(W ∩ {φ ∈ arc}), duty = \|arc\| / 2π | gated ITV, duty cycle |
| Tilted slice | W̃ ∩ {z − z_s = v·(t − t_s)} | what a moving-table CT samples |

W̃ is the same tube unrolled along real time t ∈ ℝ (the universal cover of S¹); it repeats every breathing period.

In the 4D-Lung data, phase 0 % is end-inhale and 50 % is end-exhale: in patient 101 the contoured lung volume is largest at 0 % (4.42 L) and smallest at 50 % (3.87 L) [D from the RTSTRUCTs].

### A.2 The six candidate ideas, checked

| # | Idea | Verdict | Standard clinical concept, or our metaphor? | Evidence |
|---|---|---|---|---|
| i | 4D-CT = 3 space + phase, ℝ³ × S¹; the tumour is a 4D worldtube | **Correct** | "4D-CT", "4D radiotherapy" and "respiratory phase" are standard. **"Worldtube" and "ℝ³ × S¹" are our language.** Clinicians say "tumour trajectory" and "motion envelope". | Vedam 2003: images are "sorted into an image bin that corresponded with the phase of the respiratory cycle… The complete set of such image bins accumulated over a respiratory cycle constitutes a 4D CT dataset" [V]. TG-76: "Generally 8 to 25 complete CT datasets are reconstructed" [V]. |
| ii | A time-slice = anatomy at one breathing phase | **Correct, with a caveat** | Standard. | Each phase volume is itself stitched from different breaths at different couch positions, so it is not a true snapshot. 45 of 50 patients (90 %) had at least one artefact, mean size 11.6 mm [V: Yamamoto 2008]. |
| iii | Projection along phase = ITV / MIP, the 3D "shadow" | **Correct** | The union of GTVs over phases and the MIP are standard tools. Calling them a "shadow of a 4D object" is our framing, but it is mathematically exact. | Ezhil 2009: "IGTV, defined to be the envelope of respiratory motion of the gross tumor volume", built by "combining the gross tumor volume (GTV) contours from ten respiratory phases" [V]. TG-76: "The MIP image… is the maximum CT number found in a given voxel in the set" [V]. Precise wording: union of GTVs = **IGTV**; ICRU's ITV = CTV + internal margin [S]. |
| iv | A tilted hyperplane mixing z and t = what a free-breathing helical CT samples | **Correct for fast moving-table scans** | The artefact is standard knowledge. **The "tilted hyperplane" picture is our derivation.** | Rietzel 2005: conventional CT has "severe motion artifacts that result from interplay effects between the advancing scan plane and object motion" [V]. Chen 2004: spheres moving 1 cm with a 4 s period were "shortened by as much as 2 cm", shape "significantly distorted", centre "displaced by as much as ±0.8 cm" [V]. **Slow CT is different:** it is a time-average per slice (a blurred shadow), not a tilted slice [V: TG-76 §"Slow CT scanning"]. |
| v | A rotation in a space–time plane shows the trajectory / hysteresis loop as a static shape | **Correct as a visualisation** | Trajectory and hysteresis are standard. The 4D-rotation framing is ours. | Seppenwoolde 2002: "The 3D trajectory of the tumor showed hysteresis for 10 of the 21 tumors, which ranged from 1 to 5 mm" [V]. Seen along the time axis the worldline is the loop; seen side-on it is the breathing trace [D]. |
| vi | Mid-ventilation / time-weighted mean position | **Correct** | Standard (NKI concept). | Wolthaus 2006: the mid-ventilation scan "represented the tumor in its time-averaged position over the respiratory cycle" [V]. In our terms it is the centroid of the dwell-time shadow ρ [D]. |

### A.3 Real numbers

**Tumour motion** [V unless tagged]

| Quantity | Value | Source |
|---|---|---|
| Lower-lobe tumours not attached to chest wall or vertebrae, cranial–caudal | 12 ± 2 mm (SD) | Seppenwoolde 2002 |
| Lateral and anterior–posterior, upper and lower lobes | 2 ± 1 mm | Seppenwoolde 2002 |
| Hysteresis (inhale path vs exhale path) | 1–5 mm in 10 of 21 tumours | Seppenwoolde 2002 |
| Cardiac-induced motion | 1–4 mm in 7 of 21 tumours, mostly lateral | Seppenwoolde 2002 |
| Time-averaged position | "closer to the exhale position, because the tumor spent more time in the exhalation" | Seppenwoolde 2002 |
| Lower lobe vs middle/upper lobe, SI | 18.5 (9–32) mm vs 7.5 (2–11) mm | Barnes, in TG-76 Table 2 |
| Lower / middle / upper lobe, SI | 9.5 / 7.2 / 4.3 mm | Plathow, in TG-76 Table 2 |
| Whole range reported, SI | 0–50 mm | Chen, in TG-76 Table 2 |
| Tumours moving > 5 mm (SI / lateral / AP) | 39.2 % / 1.8 % / 5.4 % of 166 tumours | Liu 2007 |
| Tumours moving > 10 mm SI | 10.8 % | Liu 2007 |
| 95th percentile of motion (SI / lateral / AP) | 13.4 / 4.0 / 5.9 mm | Liu 2007 |
| Motion-management threshold | "A greater than 5 mm range of motion is observed in any direction" | TG-76 recommendation |
| Diaphragm, shallow breathing | 12–20 mm in most series | TG-76 Table 3 |

**Shadow (ITV) versus tumour, and margins**

| Quantity | Value | Source |
|---|---|---|
| ITV / mean GTV in the 4D-Lung data (first study, 20 patients) | median 1.35, range 1.00–1.83 | [D] my computation, section C.2 |
| IGTV from MIP vs from all 10 phases, stage I | 14.14 vs 16.60 cm³ (MIP is 15 % smaller) | [V] Ezhil 2009 |
| Same, stage III | 193.3 vs 210.0 cm³ (8 % smaller) | [V] Ezhil 2009 |
| ITV(10 phases) / ITV(MIP), 12 stage I patients | 1.07 ± 0.05; centre of mass within 0.4 mm | [V] Underberg 2005 |
| Single CT + 10 mm margin vs individualised ITV + 3 mm | PTVs "on average twice the size… but still led to an underdosing in the two most mobile tumors" | [V] Underberg 2004 |
| 4D target (15 mm total margin, 5 mm internal) vs helical CT (20 mm, 10 mm internal) | 23 % smaller on average | [V] Rietzel 2006 |
| PTV from ITV vs conventional free-breathing PTV | +6 % | [V] Wolthaus 2008 |
| PTV from mid-position or exhale gating vs conventional | −10 % | [V] Wolthaus 2008 |
| Mid-ventilation: irradiated volume reduction, 40 mm tumour | 20 % | [V] Wolthaus 2006 |
| Position error of a free-breathing CT vs the mean position | SD 3.9 mm (mid-ventilation: 1.1 mm) | [V] Wolthaus 2006 |
| Margin recipe | 2.5 Σ + 0.7 σ (for a 3.2 mm penumbra) | [V] van Herk 2000 |
| Breathing as a random error | SD ≈ 0.36 × peak-to-peak for cos² , cos⁴ and cos⁶ | [D] section C.4 |
| Illustrative sphere, 10 mm diameter, 15 mm motion | GTV 0.52 cm³, ITV 1.70 cm³ (×3.25), **core empty** | [D] |
| Illustrative sphere, 20 mm diameter, 12 mm motion | GTV 4.19 cm³, ITV 7.96 cm³ (×1.90), core 0.87 cm³ | [D] |

**Imaging artefacts**

| Quantity | Value | Source |
|---|---|---|
| Helical CT of a moving sphere (1 cm amplitude, 4 s period) | shortened by up to 2 cm; centre displaced ± 0.8 cm; "clearly observable even if the amplitude is decreased to 0.5 cm" | [V] Chen 2004 |
| Our slicer on a 20 mm sphere moving 15 mm, table speed 20 mm/s | apparent SI length 11.9–34.8 mm depending on the phase at which the scan passes | [D] `slicer_proto.py` |
| Same at 40 mm/s / at 1000 mm/s | 14.6–29.8 mm / 19.7–20.3 mm | [D] |
| 4D-CT data-sufficiency condition | data must be collected "at each location for the duration of a breathing cycle plus the duration of data acquisition for an image reconstruction" | [V] Pan 2005 |
| 4D-CT residual precision (phantom) | about 2.5 mm | [V] Rietzel 2005 |
| 4D-CT scan time and dose | about 1 minute on a 16-slice scanner; dose 2–15 times a standard CT if not optimised | [V] TG-76 |

**Navigational bronchoscopy (CT-to-body divergence)**

| Quantity | Value | Source |
|---|---|---|
| Definition | "the difference between the static preprocedural CT reconstructions and the dynamic, breathing lung during the bronchoscopic procedure" | [V] Pritchett 2020 |
| Nodule displacement, full inspiration to end-exhalation at tidal breathing | 17.6 mm average, 85 lesions in 46 patients; lower lobes move significantly more | [V] Chen 2015 |
| Range by lobe | < 6 to 30 mm upper lobes; < 6 to 60 mm lower lobes | [V] Pritchett 2020 citing Chen 2015 |
| Divergence measured against intraprocedural imaging | 14.1 mm in one study | [V] Pritchett 2020 |
| Guided bronchoscopy diagnostic yield, pooled | 70 % (3,052 lesions, 39 studies) | [V] Wang Memoli 2012 |
| Electromagnetic navigation, 12-month yield | 73 % (NAVIGATE, 1,157 biopsied subjects) | [V] Folch 2019 |
| Transthoracic needle biopsy, pooled | 92.1 % | [V] Pritchett 2020 |
| Navigation with divergence correction vs standard | 79.1 % vs 54.4 % (P = 0.0019) | [V] Pritchett 2020 |
| CBCT with augmented fluoroscopy | 83.7 % per lesion (93 lesions, median 16 mm) | [V] Pritchett 2018 |

Honest caveat for the narration: breathing is one cause of divergence among several. Atelectasis under anaesthesia (87–100 % of healthy adults within minutes of induction), the scope wedging the airway, and the delay since the planning CT also contribute [V: Pritchett 2020].

### A.4 How this maps onto the challenge's own words

| Challenge text | 4D toybox (tesseract) | Lung blade |
|---|---|---|
| "a four-dimensional object can be projected into our 3D world" | perspective or orthographic shadow of the tesseract | ITV shell = shadow of the worldtube along phase; occupancy shells show where the shadow is "darker" |
| "explorer… that the user can rotate, **slice**, and project" | cross-sections: tetrahedron, truncated tetrahedron, octahedron | one breathing phase = one slice; scrubbing phase moves the slice |
| "rotations happen in planes we cannot directly perceive" | xw, yw, zw rotations | **z–t tilt = table speed of the scanner**; y–t rotation turns the breathing trace into the hysteresis loop |
| "objects can pass through each other without collision" | two cubes separated in w | the 3D shadows of a tumour and a vessel can overlap although their 4D tubes never touch; a needle that looks clear in one snapshot can hit at another phase |
| "A 4D-based data visualization tool for high-dimensional datasets" | — | 4D-CT is a real 4D dataset in clinical use |
| "Mathematical correctness and coherence" | exact polytope slices | every view is one of the set operations in A.1, and the numbers on screen are volumes of those sets |

### A.5 What we must not claim

- **It is not curved.** ℝ³ × S¹ is flat; only the topology is non-trivial (the phase axis wraps), the same class as the Glued Room. It qualifies because the challenge lists "4D Projections" as its own category. The curvature in the lung blade comes from the H² airway wing.
- **Space–time rotations are not symmetries.** Time and millimetres have different units, so a rotation angle depends on a chosen conversion constant c (mm per second). In the tesseract the rotations are true isometries. For the lung we give the tilt a physical meaning instead: the slope of the slicing hyperplane is the scanner's table speed (section D.5).
- **The phase circle is a model.** Real breathing is irregular: mean position, amplitude and period all show "significant cycle-to-cycle variations" [V: George 2005]. The worldtube is the average breath that phase sorting constructs.
- **ITV is not the only strategy.** Mid-position planning gives smaller volumes than ITV [V: Wolthaus 2008]. We show both the shadow's outline and its density and let the trainee compare.
- **Contour noise inflates ITV / GTV.** In the 4D-Lung data patient 108 moves under 3 mm yet has a ratio of 1.63, because the oncologist's contours differ slightly between phases [D].

---

## B. Why it is useful

### B.1 What a static 3D picture gets wrong, and what the 4D view shows

| Belief formed from a static 3D model | What is actually true | What the trainee sees in 4D |
|---|---|---|
| "The CT is a snapshot of the tumour." | A free-breathing scan can shorten, lengthen or shift the tumour by centimetres [V: Chen 2004]; its position error against the mean is 3.9 mm SD [V: Wolthaus 2006]. | Tilt the slicing plane and watch the snapshot distort; the slice is coloured by the phase each part was taken at. |
| "A generous uniform margin is safe." | A 10 mm margin doubled the treated volume and still under-dosed the two most mobile tumours [V: Underberg 2004]. | The shadow is long in one direction and tight in the others; a sphere of margin both overshoots and misses. |
| "Tumours move a lot" or "they don't move". | It depends on location: 12 ± 2 mm in free lower-lobe tumours, 2 ± 1 mm sideways; only 10.8 % of 166 tumours exceeded 10 mm [V]. | Two tubes side by side: a real, nearly straight one and an illustrative wavy one. |
| "The average position is halfway between inhale and exhale." | The tumour dwells near exhale [V: Seppenwoolde 2002]. For a cos⁴ breath the mean sits 37.5 % of the way from exhale to inhale, and 47 % of the time is spent within the 20 % of travel nearest exhale [D]. | The dwell-time shadow is darker at the exhale end. |
| "Out and back along the same path." | Half of tumours show 1–5 mm hysteresis [V]. | Look along the time axis: the tube's centre line is a loop, not a line. |
| "If the needle tip is in the nodule on the CT, it is in the nodule." | Planning CT is at full inspiration; the procedure is at tidal breathing; nodules shift 17.6 mm on average [V: Chen 2015]. For a 10 mm nodule moving 15 mm no point is inside at every phase [D]. | The core (always-inside region) is small or empty; the needle point's own time-line pierces the tube for only part of the cycle. |
| "Gating is free." | Gating to 30–70 % removes 88 % of a cos⁴ motion but keeps the beam on only 40–50 % of the time [D]. | Drop two flags on the tube; the shadow shrinks and the duty cycle is printed. |

### B.2 Small in-headset tasks that measure the benefit

| Task | What the user does | Score (computed from precomputed tables) | Cost to build |
|---|---|---|---|
| **M2 Needle dwell point** (recommended) | Places a point (needle tip) where it will be inside the tumour for the largest share of the breath. | Hit fraction H(p) = ρ(p), read from the occupancy texture. Best possible = max ρ. | Low: one texture lookup. |
| **M1 Margin fit** | Sizes an ellipsoid (three radii) to cover the moving tumour with the least volume. | Coverage C = min_φ \|G(φ) ∩ E\| / \|G(φ)\|; excess O = \|E\| / \|ITV\|. Pass if C ≥ 0.95. | Medium: voxel test against 10 masks on a 64³ grid. |
| **M4 Gating window** | Chooses a phase arc. | ITV reduction against the best arc of the same duty cycle. | Low: 10 × 10 table lookup. |
| **M3 Spot the artefact** | Picks the true tumour shape among three (true snapshot, slice taken with the motion, slice against it). | Correct / incorrect, before and after the toybox. | Low: three static meshes. |

### B.3 A pilot we can honestly run in 36 hours

- **Design:** within-subject, two conditions (static 3D model versus 4D view), two cases (A, B), counterbalanced order. Six to ten hackers or mentors.
- **Measures:** M2 hit fraction (or M1 coverage and excess), completion time, one Single Ease Question (1–7), and the Fast Motion Sickness score the Build Pipeline already logs.
- **Claim we may make:** "In a pilot with n people, the 4D view changed hit fraction from X to Y." **Claim we may not make:** anything about clinical performance or trained clinicians.
- I found evidence that virtual environments are used and evaluated in radiotherapy teaching (VERT) [S], but **no study of 4D tumour-motion teaching in VR**. The pilot is our own evidence; say so.

---

## C. Data usable this weekend

### C.1 Options

| Dataset | Licence | Access friction | What we get | Hours to usable meshes |
|---|---|---|---|---|
| **TCIA 4D-Lung** | CC BY 3.0 [V] | None. Public REST API, no login [V: I downloaded from it]. | 20 patients; 10-phase 4D-CT; tumour contours on every phase; 512 × 512, 0.977 mm pixels, 3 mm slices [V]. | **3–4 h** for the first tube [E] |
| **AeroPath** | Zenodo record: CC BY 4.0; GitHub README says MIT [V, the two disagree] | None. 5.04 GB zip on Zenodo, also on Hugging Face [V]. | 27 static CTs with **airway and lung labels** (NIfTI); pathological lungs; baseline reaches "at least the fifth airway generation" [V]. | 1–2 h for a real airway mesh [E] |
| LIDC-IDRI | CC BY 3.0 [V] | None. 133 GB total; take one case. | 1,010 static CTs with nodule annotations in XML [V]. No motion. | 1–2 h; not needed for 4D |
| DIR-Lab 4DCT | No licence text posted [V] | Request form, then an e-mailed password for Dropbox [V]. | 10 cases with expert landmarks; no tumour contours [V]. | Unknown wait; **skip** |
| POPI-model / CREATIS | No licence text on the page [V] | Direct download links [S]. | 1 + 6 4D-CT sets, landmarks, deformation fields [V]. | 2–3 h; licence unclear, **skip for shipping** |
| ATM'22 | "redistribution of this dataset is strictly prohibited" [V] | Signed agreement by e-mail [V]. | 500 CTs with airway labels [V]. | **Not this weekend** |
| 4D XCAT phantom | Paid Duke licence [S] | Request and fee. | Parametric breathing phantom. | **Not this weekend** |
| **Procedural (C.4)** | Ours | None | Airway tree + breathing model, labelled illustrative. | 2–3 h [E] |

### C.2 TCIA 4D-Lung in detail (queried 3 Oct 2026)

Facts from the collection page [V]: 20 locally advanced NSCLC patients; 589 studies; 6,690 series; 347,330 images; 183.04 GB; CT and RTSTRUCT; 16-slice helical Philips Brilliance Big Bore; 10 phases (0–90 %, phase-based binning); 3 mm slices; audio-visual biofeedback during every acquisition; "A single Radiation Oncologist delineated targets and organs at risk in all 4D-FBCT… on all 10 phases per scan." Of the studies, 82 are 4D fan-beam CT and 507 are 4D cone-beam CT [V: Hugo 2017].

Facts from the API and the files [V for structure, D for the numbers]:

- Each contoured 4D fan-beam study (I found 80) holds **10 CT series and 10 RTSTRUCT series, one per phase**. A study is 400–890 MB of DICOM.
- ROI names are `Tumor_c00`, `Tumor_c10`, … `Tumor_c90`, and likewise `LN_`, `RLung_`, `LLung_`, `Heart_`, `Esophagus_`, `Cord_`, `Trachea_`, `Carina_`, `MarkerA/B/C_`, `Vertebra_`.
- **Tumour contours are on all 10 phases in 20 of 20 patients.** Organs at risk are not: many patients have them on phase 0 % only.
- Download speed observed: 10 RTSTRUCT files in 5–13 s; one CT phase (149 slices, 78 MB, 28.5 MB zipped) in 12.7 s. A whole study is about 2–3 minutes.

Per-patient table, first fan-beam study, contours rasterised at 1 mm in-plane on the 3 mm slices [D]:

| Patient | Centroid range LR / AP / SI (mm) | Mean GTV (cm³) | ITV (cm³) | ITV / GTV | Core (cm³) | Gated 30–70 % (cm³) | Contoured on all 10 phases besides tumour | Study (MB) |
|---|---|---|---|---|---|---|---|---|
| 100 | 0.7 / 1.5 / 2.4 | 76.0 | 89.6 | 1.18 | 63.3 | 83.7 | lungs, heart, oesophagus, cord, trachea, nodes, markers | 768 |
| **101** | **3.1 / 3.1 / 6.8** | 30.4 | 47.4 | 1.56 | 17.2 | 41.5 | **lungs, heart, oesophagus, cord, trachea, nodes, markers** | 802 |
| 102 | 1.4 / 1.1 / 1.4 | 174.9 | 198.3 | 1.13 | 154.8 | 176.1 | lungs, heart, oesophagus, cord, nodes, markers | 715 |
| 103 | 0.8 / 0.8 / 5.8 | 58.6 | 77.8 | 1.33 | 42.5 | 65.6 | lungs, heart, oesophagus, cord, nodes, markers | 633 |
| 104 | 3.9 / 3.2 / 4.1 | 46.2 | 84.5 | 1.83 | 17.1 | 65.2 | markers only | 545 |
| 105 | 1.7 / 2.2 / 7.8 | 33.4 | 57.7 | 1.73 | 15.0 | 47.5 | markers only | 597 |
| 106 | 3.2 / 4.7 / 4.9 | 143.7 | 181.6 | 1.26 | 114.1 | 171.0 | markers only | 545 |
| 107 | 0.2 / 1.6 / 2.3 | 18.1 | 21.3 | 1.18 | 15.6 | 21.1 | lungs, oesophagus, nodes | 452 |
| 108 | 2.8 / 1.1 / 1.8 | 10.2 | 16.7 | 1.63 | 5.4 | 13.8 | nodes | 629 |
| 109 | 0.4 / 0.6 / 0.2 | 310.7 | 316.8 | 1.02 | 300.9 | 316.3 | — | 592 |
| 110 | 0.7 / 5.0 / 5.8 | 54.2 | 80.0 | 1.48 | 32.1 | 76.2 | nodes | 597 |
| 111 | 1.0 / 4.3 / 4.6 | 75.1 | 102.2 | 1.36 | 53.2 | 93.2 | nodes | 602 |
| 112 | 0.0 / 0.0 / 0.0 | 31.4 | 31.4 | 1.00 | 31.4 | 31.4 | nodes (**tumour contours are identical copies**) | 623 |
| 113 | 0.7 / 1.7 / 4.6 | 64.4 | 86.0 | 1.34 | 48.2 | 81.1 | nodes | 522 |
| **114** | 0.5 / 1.4 / **11.3** | 178.2 | 231.5 | 1.30 | 123.9 | 210.1 | nodes | 694 |
| 115 | 0.6 / 1.4 / 3.6 | 7.0 | 9.5 | 1.37 | 4.4 | 9.1 | nodes | 550 |
| 116 | 1.3 / 2.3 / 2.8 | 31.4 | 44.1 | 1.40 | 24.6 | 41.5 | nodes | 624 |
| 117 | 1.0 / 2.1 / 4.5 | 10.3 | 15.2 | 1.48 | 6.0 | 13.2 | nodes | 406 |
| 118 | 4.7 / 2.5 / 6.5 | 13.3 | 23.3 | 1.75 | 5.8 | 19.7 | nodes | 469 |
| 119 | 0.6 / 0.5 / 0.7 | 117.3 | 122.2 | 1.04 | 112.8 | 121.7 | nodes | 723 |

Reading the table:

- Median SI range 4.3 mm; 6 of 20 exceed 5 mm; 1 of 20 exceeds 10 mm. This agrees with Liu 2007 for locally advanced disease [V].
- SI values are quantised by the 3 mm slices; treat them as ± 1 mm.
- **Patient 101** is the recommended real case: the tumour moves 6.8 mm, the right lung's lower border moves 30 mm and the left 24 mm, the contoured lung volume changes by 0.55 L, and every organ is contoured on every phase, so the whole breathing chest can be shown without running a segmentation model.
- **Patient 114** has the largest tumour motion (11.3 mm) but a 178 cm³ tumour and organs on phase 0 % only.
- **Do not use patient 112** for motion (contours copied across phases) or 109 and 119 (no measurable motion).

Download recipe for patient 101's study (`StudyInstanceUID 1.3.6.1.4.1.14519.5.2.1.6834.5010.300895057088920888884721386157`) [V: I ran these endpoints]:

```bash
# list the 20 series (10 CT + 10 RTSTRUCT) of patient 101
curl "https://services.cancerimagingarchive.net/nbia-api/services/v1/getSeries?Collection=4D-Lung&PatientID=101_HM10395&format=json"
# download one series as a zip (repeat for the 20 SeriesInstanceUIDs whose model name is Pinnacle3)
curl -o ph00.zip "https://services.cancerimagingarchive.net/nbia-api/services/v1/getImage?SeriesInstanceUID=<uid>"
```

Alternative: the collection is mirrored in the NCI Imaging Data Commons; `pip install idc-index` then `idc download …` needs no login [S]. Do not download the whole collection.

Required credit [V]: Hugo et al. (2016), *Data from 4D Lung Imaging of NSCLC Patients*, TCIA, doi 10.7937/K9/TCIA.2016.ELN8YGLE, plus the TCIA citation. Put it in the app's credits and on Devpost.

### C.3 TotalSegmentator on the RTX 5070

- Apache-2.0 for the open tasks `total`, `body`, `lung_vessels`, `lung_nodules` [V].
- `total` gives the five lung lobes (`lung_upper_lobe_left`, `lung_lower_lobe_left`, `lung_upper_lobe_right`, `lung_middle_lobe_right`, `lung_lower_lobe_right`), `trachea`, `heart`, `aorta`, `pulmonary_vein`, ribs and vertebrae [V].
- `lung_vessels` gives `lung_arteries`, `lung_veins`, `lung_airways`, `lung_airways_wall` [V]. The Build Pipeline already records that the airway output stops at generations 1–2.
- Options that matter: `--fast` (3 mm model), `--roi_subset`, `--force_split` [V].
- Run time: the README's table is for an RTX 3090 and is an image I could not read. Expect roughly 1 minute per volume, so about 10 minutes for 10 phases [E]. The 5070 needs a PyTorch build with support for the 50-series; install and test it before the event.
- **With patient 101 it is optional**, because lungs and heart are already contoured on every phase. Use it only for lobes.

### C.4 Fully procedural fallback, labelled "illustrative"

**Airway tree** (symmetric Weibel-type with Kitaoka-style rules):

| Parameter | Value | Source |
|---|---|---|
| Branching | dichotomous; N(g) = 2^g | [S] Weibel |
| Diameter ratio child / parent | 2^(−1/3) = 0.794 | [V] Montesantos 2016, Eq. 3 |
| Length / diameter | 3 ± 1 | [V] Montesantos 2016, Eq. 4 |
| Branching angle (child vs parent axis) | usually below 60°; optimum 37.28° | [V] Montesantos 2016 |
| Rotation between successive branching planes | 90° assumed in models; 88.1° ± 41.6° measured | [V] Montesantos 2016 |
| Generations to an acinus | mean about 15–17, range 8–25 | [V] Montesantos 2016, Table 2 |
| Number of acini (one per terminal bronchiole) | about 23,000–30,000 in models and Horsfield's cast; Weibel's symmetric model: 66,000 ≈ 2¹⁶ | [V] Montesantos 2016, Table 2 |
| Asymmetry (optional) | flow split r, d₁ = d₀ r^(1/n), d₂ = d₀ (1 − r)^(1/n), n ≈ 2.8 | [S] Kitaoka 1999, recalled, not fetched |
| Trachea | diameter about 18 mm, length about 120 mm | [S] Weibel, not fetched |

Symmetric optimum check: with d₁ = d₂ = 0.794 d₀ the minimum-work branching angle is arccos(2^(−1/3)) = 37.5° [D], matching the 37.28° above.

```python
def grow(node, g, g_max=10):
    if g == g_max: return
    d = node.d * 2 ** (-1/3) * jitter(0.9, 1.1)      # diameter ratio 0.794
    L = 3.0 * d * jitter(0.75, 1.25)                 # length / diameter = 3 ± 1
    plane = rotate(node.plane, node.dir, radians(90)) # turn the branching plane 90 degrees
    for sign in (+1, -1):
        dir = rotate(node.dir, plane.normal, sign * radians(37))
        child = Node(pos=node.end, dir=dir, d=d, L=L, plane=plane, gen=g + 1)
        if inside_lung_hull(child.end): node.children.append(child); grow(child, g + 1, g_max)
```

Stop at generation 8–10 for rendering (511–2,047 branches). The H² wing instantiates branches lazily by address, so the full 23 generations are never held in memory.

**Breathing model** (Lujan, as used by Seppenwoolde):

```
z(t) = z₀ − b · cos^(2n)( π t / τ − φ₀ )        z₀ = exhale position, b = peak-to-peak, τ = period
```

The original abstract describes "a periodic but asymmetric function (more time spent at exhale versus inhale)" [V: Lujan 1999]; George 2005 tested n = 1, 2, 3 with parameters z₀, b, τ [V]; the explicit formula is from secondary citations [S].

| n | Mean of cos^(2n) | Mean position | SD | Share of time within 20 % of travel from exhale | Peak speed |
|---|---|---|---|---|---|
| 1 | 0.500 | z₀ − 0.500 b | 0.354 b | 30 % | 3.14 b/τ |
| 2 | 0.375 | z₀ − 0.375 b | 0.364 b | 47 % | 4.08 b/τ |
| 3 | 0.313 | z₀ − 0.313 b | 0.358 b | 55 % | 4.88 b/τ |

All [D]; the mean is C(2n, n) / 4ⁿ.

Whole-lung displacement field for the illustrative case:

```
u(x, t) = ( A_LR , A_AP(x) , A_SI(x) ) ⊙ cos^(2n)( π t/τ − φ_axis )
A_SI(x) = A_apex + (A_base − A_apex) · s(x),   s = 0 at the apex, 1 at the diaphragm
A_apex ≈ 3 mm, A_base ≈ 15 mm, A_AP ≈ 1–5 mm (larger anteriorly), A_LR ≈ 1–2 mm
φ_AP = φ_SI + δ          a phase lag δ between axes produces hysteresis
```

Amplitudes are chosen inside the ranges in TG-76 Tables 2 and 3 [V]; the linear gradient is our simplification [E]. TG-76 states that "the phase difference is what leads to the hysteresis effect" [V]. For n = 1 the loop width at mid-travel is b_AP · sin(2δ) [D]; b_AP = 4 mm and δ = 24° give a 3 mm loop.

Phase-bin positions for b = 15 mm, n = 2 (mm from exhale): 15.0, 12.3, 6.4, 1.8, 0.1, 0.0, 0.1, 1.8, 6.4, 12.3 [D]. The largest step between neighbouring bins is 5.9 mm, so interpolate four sub-steps between phases when building the shadow of a small nodule, or the shell looks scalloped [D].

### C.5 Recommendation

- **Real case:** 4D-Lung patient 101. Label: "Public research data, TCIA 4D-Lung, CC BY 3.0. Not for clinical use."
- **Illustrative case:** a 10 mm sphere in the lower lobe, b = 15 mm, n = 2, 3 mm hysteresis. Label: "Illustrative model."
- **Airway:** procedural tree for the H² wing (the Build Pipeline's decision), optionally one AeroPath airway mesh as the "real" proximal tree once the licence wording is settled.

---

## D. How to build it on the decided stack

### D.1 Representation: which one

| Representation | Any hyperplane? | Handles topology change (tube cut into two pieces)? | Needs vertex correspondence? | Cost on phone | Verdict |
|---|---|---|---|---|---|
| 10 per-phase meshes, shown one at a time or all at once | No, phase slices only | — | No | trivial | **Minimal** |
| Template mesh + per-phase positions (blend shapes or vertex-animation texture) | No, phase slices only (exact lerp between phases) | No | Yes | trivial | Use for the breathing lungs |
| **Template mesh × phases → prisms → tetrahedra, sliced on the CPU** | **Yes** | **Yes** | Yes | about 0.5–1 ms for 15–30k tets [E] | **Target** |
| Same tetrahedra, Engine4D vertex-shader slicer | Yes | Yes | Yes | 4 vertices and 320 bytes per tet; no CPU cost [D] | Stretch, above about 50k tets |
| 4D signed-distance texture (64³ × 10, R8, 2.6 MB), ray-marched | Yes | Yes | **No** | 16–32 steps per covered pixel; risky when the object fills the view [E] | Stretch |
| Precomputed slice meshes at fixed angles | Discrete steps only | Yes | Yes (offline) | trivial | **Fallback** if the live slicer slips |

Per-phase meshes extracted from contours have no vertex correspondence, and a per-vertex time lookup cannot represent a slice that cuts the tube twice. The tetrahedral tube handles both, which is why it is the target.

### D.2 Offline pipeline (PC, Python; `atlas case tube4d`)

```python
# inputs: 10 CT phases + 10 RTSTRUCT (4D-Lung) or the analytic Lujan sphere
for k in range(10):
    mask[k] = rasterise(rtstruct[k], f"Tumor_c{10*k:02d}")         # CT grid 0.977 x 0.977 x 3 mm
roi = bbox(union(mask)) + 15 mm
for k in range(10):
    sdf[k] = edt(~mask[k], sampling=vox) - edt(mask[k], sampling=vox)   # negative inside
    sdf[k] = gaussian(resample_iso(sdf[k], 1.0 mm), sigma=1.0)          # shape-based interpolation between 3 mm slices

# shadow, core, dwell-time shadow (interpolate 4 sub-steps between phases, wrapping 9 -> 0)
sdf_itv  = min over k and sub-steps of sdf        # union   (use max if your SDF is positive inside)
sdf_core = max over k of sdf                      # intersection
rho      = mean over k of (sdf[k] < 0)            # occupancy in [0, 1] -> 64^3 R8 texture
itv_table[a][b] = volume(union(mask[a..b]))       # 10 x 10 gating arcs

# template with correspondence (tumours move almost rigidly)
V0, F = decimate(marching_cubes(mean(sdf), 0), target_tris=600)
for k in range(10):
    V[k] = V0 + (centroid[k] - centroid_mean)
    repeat 5: V[k] -= sample(sdf[k], V[k])[:, None] * normalise(grad(sdf[k], V[k]))   # Newton projection onto phase k

# 4D vertices: phase 10 repeats phase 0 so the tube closes
P[k, i] = (M * V[k % 10][i], tau = L * k / 10)       for k = 0..10
T = prisms_to_tets(F, layers=10)                     # D.3
write tube4d.bin (P float32 Nx4, T int32 Mx4, slab id per tet), phases.glb (blend shapes),
      itv.glb, core.glb, occ.r8, table.json (volumes, centroid path, ranges)
```

M is the display magnification (about 15, so a 30 mm tumour is 0.45 m) and L the room length of one breath (1.5 m). Libraries: pydicom, rt-utils or the 60-line rasteriser in `R2_assets/rt_motion.py`, scipy, scikit-image, trimesh. An "as contoured" tube and a "rigid shape" tube (phase-0 shape translated along the centroid path) should both be exported; the rigid one hides contouring noise and must be labelled as such.

### D.3 Prisms to tetrahedra

Each surface triangle (a, b, c) between phase layers k and k + 1 is a triangular prism. Sort the triangle's vertex indices so a < b < c, then:

```
tets = (a_k, b_k, c_k, c_k+1), (a_k, b_k, b_k+1, c_k+1), (a_k, a_k+1, b_k+1, c_k+1)
```

Sorting by global index makes neighbouring prisms choose the same diagonal on their shared quad, so the tube has no cracks [D; checked in `slicer_proto.py`]. Count: triangles × 10 layers × 3. A 600-triangle template gives 18,000 tetrahedra per breath.

The tesseract is the same data type: its boundary is 8 cubes, each split into 6 tetrahedra, 48 in all [D], matching the Build Pipeline.

### D.4 Slicing a tetrahedral mesh by a hyperplane

For each tetrahedron compute the four signed distances s_i = n · p_i − d. No vertex or all four positive: nothing. One or three positive: a triangle on the three edges at the lone vertex. Two and two: a quad on the four mixed edges, in the order (a,c), (a,d), (b,d), (b,c). Each crossing point is p = p_i + (s_i / (s_i − s_j)) (p_j − p_i), mapped to 3D by the hyperplane's orthonormal basis.

```csharp
[BurstCompile]
struct SliceTetsJob : IJobParallelFor {
    [ReadOnly] public NativeArray<float4> P;      // 4D vertices (x, y, z, tau), metres
    [ReadOnly] public NativeArray<int4>   T;      // tetrahedra
    [ReadOnly] public NativeArray<int4x2> Lut;    // 16 entries: four edges as (i0..i3),(j0..j3); degenerate for masks 0 and 15
    public float4 n; public float d;              // hyperplane n . p = d, |n| = 1
    public float4 e0, e1, e2, p0;                 // orthonormal basis of the hyperplane, and a point on it
    [NativeDisableParallelForRestriction] public NativeArray<SliceVert> Out;   // 4 per tet, fixed slots

    public void Execute(int i) {
        int4 t = T[i];
        float4x4 q = new float4x4(P[t.x], P[t.y], P[t.z], P[t.w]);            // columns = the 4 vertices
        float4 s = math.mul(n, q) - d;                                         // 4 signed distances
        int mask = math.bitmask(s > 0f);
        int4x2 e = Lut[mask];
        for (int v = 0; v < 4; v++) {
            float4 a = q[e.c0[v]], b = q[e.c1[v]];
            float sa = s[e.c0[v]], sb = s[e.c1[v]];
            float4 x = a + math.saturate(sa / (sa - sb)) * (b - a) - p0;
            Out[4 * i + v] = new SliceVert {
                pos = new float3(math.dot(e0, x), math.dot(e1, x), math.dot(e2, x)),
                tau = a.w + math.saturate(sa / (sa - sb)) * (b.w - a.w)        // phase of this point, for colour
            };
        }
    }
}
```

- Fixed output slots and a fixed index buffer (0,1,2, 0,2,3 per tet) mean no compaction and no garbage; unused quads are degenerate and cost nothing to rasterise. This is the same trick Engine4D uses on the GPU [V: `Core4D.cginc`].
- Upload with `Mesh.SetVertexBufferData`. Draw two-sided and take the normal from screen-space derivatives, which suits the unlit "architectural diagram" look and avoids winding bookkeeping.
- Run only when the hyperplane changed (dirty flag). Store a slab id per tet and skip slabs the hyperplane cannot reach; a pure phase slice touches one slab in ten.
- Cost: roughly 5 ns per rejected tet and 100 ns per intersected one, so 18–30k tets is about 0.3–1 ms on one performance core [E]. The Python prototype handles 28,800 tets in 32 ms [D].
- **Decision rule:** if the device shows more than 1.5 ms at the 95th percentile, cut the template to 300 triangles; port Engine4D only if more geometry is wanted.

**Engine4D port notes** [V: read from the repository, MIT, Unity 2022.2 project, Built-in pipeline `.cginc`]: every tetrahedron is drawn as four vertices; each vertex carries all four corners (`POSITION`, `TEXCOORD0–2`) and packed normals in an integer attribute; an 8 × 8 lookup texture indexed by `SV_VertexID % 4` and the four sign bits picks the edge; the slice is always w = 0 after a 4 × 4 camera matrix and offset. No geometry shader. Port risks: `.cginc` to URP HLSL, integer vertex attributes and `tex2Dlod` in the vertex stage on Metal, and it does not fit the SRP Batcher. Budget 3–5 hours [E].

### D.5 Hyperplanes and 4D rotations

Coordinates (x, y, z, τ) with τ = c · t in metres. The rotation by θ in the z–τ plane is

```
            | 1  0    0       0    |
R_zτ(θ) =   | 0  1    0       0    |      slice normal  n(θ) = R e_τ = (0, 0, −sin θ, cos θ)
            | 0  0  cos θ  −sin θ  |      slice basis   e₀ = e_x, e₁ = e_y, e₂ = R e_z = (0, 0, cos θ, sin θ)
            | 0  0  sin θ   cos θ  |
```

and likewise R_xτ, R_yτ. A general 4D rotation is a product of two such plane rotations.

**Physical meaning of the tilt** [D]. A scanner whose table moves at speed v images position z at time t(z) = t_s + (z − z_s)/v. That is the hyperplane z − (v/c) τ = constant, so

```
tan θ = c / v        θ = 0°  : v → ∞, an ideal snapshot (one phase)
                     θ = 90° : v → 0, one couch position watched over time (cine)
```

A sphere of diameter D moving at speed u along the table direction appears with length about D · v / (v − u): longer when it moves with the scan, shorter against it [D]. This is the Chen 2004 phantom result in one line, and it is a ready-made unit test.

**Two rotations to ship:**

| Name | Plane | Starts as | Ends as | Why this plane |
|---|---|---|---|---|
| Scanner tilt | z–τ (superior–inferior and time) | true snapshot | cine at one couch position | SI is the dominant motion axis (12 ± 2 mm against 2 ± 1 mm) and z is the table axis |
| Trace-to-loop | y–τ view rotation of the centre line | breathing trace z(t) | hysteresis loop in the sagittal plane | hysteresis lives between SI and AP |

Controls map one angle to one physical knob. Do not expose a free 4D orientation for the medical view; keep that for the tesseract.

### D.6 Shadow, dwell-time shadow, core, gating

- **ITV shell, core shell, and the ρ = 0.5 shell** are three static meshes from D.2 (about 1.5k triangles each).
- **Gated shadow without geometry processing:** draw the selected phase meshes together, opaque, in one flat colour. The depth buffer shows exactly the union's silhouette [D]. Volumes come from the 10 × 10 table.
- **Needle task:** sample the 64³ occupancy texture at the needle tip.
- **Look-down shadow (existing interaction D5):** when the user looks down, fade the tube out and the three shells in.

### D.7 Colour and look

- **Colour by phase** with a cyclic ramp (0 % and 100 % identical): one extra row in the shared 128² palette texture; `u = frac(tau / L)`. Vary lightness as well as hue so it survives colour-blindness. Inhale cool, exhale warm.
- A tilted slice is then visibly striped by phase, which explains the artefact without words: the top of the image was taken at inhale, the bottom at exhale.
- Emissive, no bloom, edge lines with `fwidth`, fog in the room's hue (all per the Build Pipeline). Dithered transparency for the ghost of the full tube behind the slice.
- **Breathing lungs:** 10 blend shapes for the minimal build; a vertex-animation texture inside the shared shader for the target build (two rows sampled and lerped, wrapping 9 → 0).

### D.8 Controls: three twins for every action

| Action | Marker cube | Gamepad | Gaze / walking |
|---|---|---|---|
| Phase (slice offset) | cube yaw | bumpers | walk along the lane, or around the ring |
| Scanner tilt θ | cube pitch, clamped 0–90° | triggers | dwell on a floor dial with five detents |
| Shadow on/off | flip the cube | Y | look down past −35° |
| Gating flags | — | A at two positions | dwell 600 ms |
| Needle point | — | stick + A | head ray + dwell |

Cube pose needs the fusion, One-Euro smoothing and quaternion sign fix already listed in the Build Pipeline.

### D.9 The Breath Room under free walking

The Spec's Breath Room assumed a 1.5 m glued cell inside the taped 3 × 3 m square. With free walking it becomes a **breath lane**:

- A straight strip on the floor along the longest clear segment of the scanned room. One breath is L = 1.5 m. The tube mesh for one period is instanced at offsets k · L (5–7 copies, the Glued Room copy code along one axis), with fog hiding the ends.
- Because phase wraps, the lane has no virtual end. The user turns round at the real wall and phase simply runs backwards.
- Room axes: (time, anterior–posterior, superior–inferior). Left–right is the axis to drop, because it carries the least motion and the hysteresis loop lies in the other two. Looking along the lane shows the sagittal shadow and the loop.
- No self-avatar copies here: they would suggest that space wraps, when it is time that wraps.

**Is it comprehensible?** Shown cold, no. People read the tube as a sausage-shaped tumour. It becomes readable in about 20 seconds if we build it in front of them:

1. Start with an ordinary breathing tumour hologram.
2. On a button press time "unrolls": each phase's copy slides to its place on the lane and leaves a trail, like a stroboscopic photograph.
3. A glowing "now" plane travels along the tube at breathing speed, showing the cross-section in step with a breathing chest.
4. The floor carries phase stripes and the labels "inhale" and "exhale".
5. The user walks; the "now" plane follows them.

Test it on three naive people on the first evening: ask "what is this tube?". Pass if two of three say "the tumour over time" unprompted. If it fails, fall back to the **ring**: ten phase meshes placed round a 0.75–1.0 m circle, a walk-around zoetrope. The ring needs no new technology and shows the closure of S¹ literally, but its time axis is bent, so hyperplane tilts are not planar there. Use the ring for scrubbing and gating, the lane for slicing.

### D.10 Performance budget and acceptance

| Item | Budget | Tag |
|---|---|---|
| Tumour tube | 600-triangle template, 18k tets per period; slice output at most 36k triangles, mostly degenerate | [D] |
| Slicer CPU | ≤ 1.0 ms at the 95th percentile on the iPhone 15 Pro Max; 0 ms when idle | [E] |
| Shells (ITV, ρ = 0.5, core) | 3 × 1.5k triangles | [E] |
| Ten ghost phase meshes | 10 × 600 triangles | [D] |
| Breathing chest (lungs, heart, trachea) | ≤ 40k triangles, one material | [E] |
| Lane copies | 5–7 × 3k triangles | [E] |
| Scene total | ≤ 120k triangles per eye, < 40 draw calls | inside the Build Pipeline's 300–500k per frame |
| Memory | tube data 0.4 MB; slice vertex buffer 1.2 MB; occupancy 0.26 MB; SDFs 2.6 MB if shipped | [D] |
| Thermal | LiDAR meshing and hand tracking off in this scene | Build Pipeline |

Acceptance tests:

- Tesseract corner-first slices have surface area 0.8660 at x + y + z + w = 0.5 and 6.9282 at 2.0 (octahedron) to four decimals [D: the prototype matches].
- A 4D ball of radius 1 sliced at offset c gives a sphere of radius √(1 − c²).
- The moving-sphere tube sliced at θ = 0 gives the sphere (area within 2 % of 4πR²); at table speed 20 mm/s its apparent length spans 11.9–34.8 mm [D].
- ITV, core and gated volumes on screen equal `table.json` within 1 %.
- Frame time at the 95th percentile ≤ 16.6 ms through a 15-minute soak with the slice moving continuously.

### D.11 Scope ladder for 4 people and about 36 hours (medical 4D only)

| Level | What ships | Person-hours [E] |
|---|---|---|
| **Minimal** | Tesseract toybox (Phase 1). Illustrative Lujan nodule generated in C#, no data pipeline: phase scrub, ITV shell, empty-core message, numbers. Patient 101 as ten per-phase meshes on the ring, shadow by drawing all phases. Labels and credits. | 10–12 |
| **Target** | Patient 101 through the D.2 pipeline to a tetrahedral tube. Live CPU slicer with the scanner tilt and phase colouring. Occupancy shells. Breath lane with the unroll animation. Breathing lungs. Task M2 with logging and plan-card numbers. | 25–30 |
| **Stretch** | Engine4D GPU slicer so lungs and proximal airway are sliceable too. Trace-to-loop rotation. Task M1 with a static-versus-4D pilot (n ≥ 8). Lane placed inside the H² lesion chamber. Second patient (114). A chest-worn second phone as a breathing surrogate driving phase. 4D SDF ray-march. | +20 |

Dependency tree (S = sequential gate, ∥ = parallel):

```
S0  hour 0–1   agree the file contract: tube4d.bin, table.json, palette row for phase, magnification M, lane length L
 ├─∥ A  engine     Mesh4D + SliceTetsJob + tests ── tesseract (Phase 1 exit) ── tube slicing (Phase 2)
 ├─∥ B  data (PC)  Lujan generator in C# (unblocks A, C, D within 2 h) ── download patient 101 ── D.2 pipeline ── pack
 ├─∥ C  rendering  phase colour ramp, slice material, shells, ghost, breathing lungs
 └─∥ D  UX / eval  controls and twins, lane and ring, unroll animation, M2 task, logging, PC dashboard twin
S1  integrate synthetic tube end to end on the phone (all four)         ── gate: 60 fps, slicer ≤ 1 ms
S2  swap in the real case; first naive-user comprehension test           ── gate: 2 of 3 understand the tube
S3  pilot, plan-card numbers, narration, credits
```

The synthetic generator is the key to parallelism: nobody waits for the data pipeline.

---

## E. One story: hyperbolic airway, 4D breath, and where the toybox fits

**The sentence:** *the lung defeats an ordinary 3D picture twice. It has too many branches to fit in flat space, and it will not hold still.* Negative curvature fixes the first; a fourth, circular dimension fixes the second.

| Beat | Geometry | What the user learns | Number on screen |
|---|---|---|---|
| 1. Toybox (onboarding, 60 s) | flat ℝ⁴, tesseract | Three verbs: **slice** (push the cross-section through), **shadow** (look down), **rotate** (tilt the cube) | cross-section name; tet count 48 |
| 2. Airway wing | H² × E | The tree keeps doubling: about 16 generations of conducting airway (the part a scope can enter), 23 counting the gas-exchange zone [S]. A hyperbolic floor has room for every branch at constant corridor width. | 2¹⁶ ≈ 66,000 terminal bronchioles in Weibel's model [V]; generation counter |
| 3. Lesion chamber: "the map was one frozen slice" | ℝ³ × S¹ | The CT used for navigation is a single slice of a 4D object. Pull time out: the lesion becomes a tube. | 17.6 mm average shift between planning CT and tidal breathing [V] |
| 4. The three verbs return | same slicer | Slice = a phase. Shadow = ITV, with a darker exhale end. Rotate = the scanner's tilt, and the artefact appears. | ITV / GTV; duty cycle; apparent length |
| 5. Decide | — | Place the needle point or the gating window; the score is a 4D set measure | hit fraction; ITV reduction |
| 6. Plan card | — | Route, robust exit, gating arc, dwell point | all of the above |

Why the two halves are one design and not two demos:

- **Same shape of argument.** In both, a flat static 3D model is the wrong container, and the fix is a different geometry chosen to match the data's structure: exponential growth needs H²; periodic motion needs S¹.
- **Same code path.** Tesseract and tumour go through one `Mesh4D` and one slicing job. The toybox is the test fixture with known answers.
- **Same decision.** The Spec already asks the trainee to "choose the airway exit that stays closest across phases". In 4D terms that is the exit whose own time-line stays nearest the tube. The hyperbolic route ends in a 4D question.
- The full model space is (H² × E) × S¹. That is our framing of the layout, not an anatomical claim.

**Phase mapping for the plan.** Phase 1 (tech, generated spaces, phone linked to PC): `Mesh4D`, the slicer, the tesseract, the procedural Lujan tube and airway generators, and the PC dashboard showing the live slice parameters over the LiteNetLib link. Phase 2 (use cases): the real patient, shells, lane, tasks and plan card.

---

## F. Conflicts and errors found in the existing documents

1. **Slice Tilt names the wrong plane.** Spec §11.2.1 says the room's axes are (time, head–feet, left–right) and that tilting "in the plane spanned by time and left–right" ends at an ordinary 3D snapshot. Both of those axes lie inside the slice, so that rotation never changes the slice. To reach a snapshot the normal must rotate from the dropped spatial axis to time. Fix: use the z–τ scanner tilt of D.5.
2. **"Precompute 7 tilted slices and blend neighbours"** contradicts the Spec's own rule that meshes of different topology are not cross-faded. Tilted slices do change topology. Fix: slice live, or step between precomputed slices without blending.
3. **Slicer location.** Spec §5.3 and Brief §3.9 specify a compute shader with an append buffer; the Build Pipeline says CPU. The Build Pipeline wins, and D.4 shows the CPU has room.
4. **"Real GTV on all 10 phases" is true; "GTV + OARs on all 10 phases" (Spec §11.3.4) is not**: only 5 of 20 patients have lungs on every phase, and patient 112's tumour contours are copies.
5. **"ITV(arc) = OR of phase masks"** under-covers small fast nodules between bins (5.9 mm steps for 15 mm motion). Interpolate sub-steps.
6. **The ≈17 mm figure** in the Spec is nodule displacement between full inspiration and end-exhalation (Chen 2015), which is one contributor to CT-to-body divergence, not the divergence itself. The one direct divergence figure I found is 14.1 mm.
7. **The 3 × 3 m square** is assumed by both the Breath Room cell and the Breathing Ring radius; D.9 restates both for free walking.
8. **AeroPath licence wording** differs between GitHub (MIT) and Zenodo (CC BY 4.0). Attribute under CC BY 4.0 to be safe.
9. **"About 23 generations"** (Brief §4.3.1) counts the gas-exchange zone. The conducting airways, which are all a bronchoscope could ever enter, are about 16 generations, and acini sit 8–25 branchings from the trachea [V: Montesantos 2016]. The exponential-growth argument for H² is unchanged; say "about 16 conducting generations, 23 in all".
10. **Real tumours in the chosen dataset move little** (median 4.3 mm SI). The Brief's illustrative 12 mm figure is a lower-lobe free-tumour value, not what the 4D-Lung cases will show. Plan the visuals for a subtle real tube and a dramatic illustrative one.

## G. Could not verify

- ICRU Reports 62 and 83 themselves (paywalled); the ITV definition is from secondary sources.
- The explicit Lujan formula in the original paper (abstract only), and Seppenwoolde's full text (which plane the hysteresis lies in, fitted n values).
- Kitaoka's nine rules and the diameter and angle equations (publisher returned 403); quoted from memory.
- Weibel's original trachea dimensions.
- Hugo 2017 full text. I verified contour presence directly, but only for the first fan-beam study of each patient.
- TotalSegmentator's run-time table and its behaviour on an RTX 5070.
- DIR-Lab and POPI redistribution terms; how many phases the DIR-Lab download contains.
- 4D XCAT price and terms.
- The Imaging Data Commons page says the collection exceeds 1 TB; TCIA says 183 GB. Irrelevant if we take one study.
- Any published evidence that VR improves understanding of 4D tumour motion specifically.
- Engine4D on iOS Metal under URP; Burst slicer timings on the A17 Pro; both are estimates.
- Typical helical table speeds, used only to pick demo values.

## H. Questions for the team

1. Hero case: real patient 101 plus the illustrative mobile nodule side by side (my recommendation), or real data only?
2. Is a labelled motion exaggeration (×2–3) acceptable on the real case, or must real data stay 1 : 1?
3. Lane (walk through time) or ring (zoetrope) as the default? How long a clear straight run will the venue floor give us?
4. Do you accept moving Slice Tilt to the z–τ scanner plane?
5. Which measured task goes on the plan card: needle dwell point (cheap) or margin fit (richer)? How many test users can we get?
6. May the Python data pipeline run before the event starts, or do the hackathon rules require all work inside the 36 hours? Is the RTX 5070 machine at the venue with PyTorch already working?
7. Is the marker cube in scope for Phase 2, or gamepad and gaze only?
8. Can anyone with clinical training read the narration once before judging?
9. Is the second phone available to act as a chest-worn breathing surrogate (stretch)?
10. Fix M = 15× magnification and L = 1.5 m per breath, or tune in the headset?

## Sources

Radiotherapy and motion
- AAPM TG-76, Keall et al. 2006: https://www.aapm.org/pubs/reports/RPT_91.pdf
- Seppenwoolde et al. 2002: https://europepmc.org/article/MED/12095547
- Liu et al. 2007: https://mdanderson.elsevierpure.com/en/publications/assessing-respiration-induced-tumor-motion-and-internal-target-vo
- Chen, Kung, Beaudette 2004: https://europepmc.org/article/MED/14752730
- Rietzel, Pan, Chen 2005: https://europepmc.org/article/MED/15895570
- Vedam et al. 2003: https://europepmc.org/article/MED/12564500
- Pan 2005: https://europepmc.org/article/MED/15789609
- Yamamoto et al. 2008: https://europepmc.org/article/MED/18823717
- Underberg et al. 2005 (MIP): https://europepmc.org/article/MED/16111596
- Underberg et al. 2004: https://europepmc.org/article/MED/15519801
- Ezhil et al. 2009 (open access): https://doi.org/10.1186/1748-717X-4-4
- Rietzel et al. 2006: https://europepmc.org/article/MED/16904528
- Wolthaus et al. 2006: https://europepmc.org/article/MED/16863933
- Wolthaus et al. 2008: https://europepmc.org/article/MED/18313530
- van Herk et al. 2000: https://europepmc.org/article/MED/10863086
- Lujan et al. 1999: https://europepmc.org/article/MED/10360531
- George et al. 2005: https://europepmc.org/article/MED/16266099

Bronchoscopy
- Pritchett et al. 2020 (open access): https://doi.org/10.21037/jtd.2020.01.35
- Chen et al. 2015: https://europepmc.org/article/MED/25357229
- Pritchett et al. 2018: https://europepmc.org/article/MED/30179922
- Folch et al. 2019 (NAVIGATE): https://europepmc.org/article/MED/30476574
- Wang Memoli et al. 2012: https://europepmc.org/article/MED/21980059

Data
- TCIA 4D-Lung: https://www.cancerimagingarchive.net/collection/4d-lung/ (doi 10.7937/K9/TCIA.2016.ELN8YGLE)
- TCIA REST API: https://services.cancerimagingarchive.net/nbia-api/services/v1/getSeries?Collection=4D-Lung&PatientID=101_HM10395&format=json
- Hugo et al. 2017: https://doi.org/10.1002/mp.12059
- Imaging Data Commons mirror: https://portal.imaging.datacommons.cancer.gov/collections/4d_lung · https://github.com/ImagingDataCommons/idc-index
- DIR-Lab 4DCT: https://med.emory.edu/departments/radiation-oncology/research-laboratories/deformable-image-registration/downloads-and-reference-data/4dct.html
- POPI-model: https://github.com/open-vv/popi-model
- LIDC-IDRI: https://www.cancerimagingarchive.net/collection/lidc-idri/
- ATM'22: https://atm22.grand-challenge.org/
- AeroPath: https://github.com/raidionics/AeroPath · https://zenodo.org/records/10069289 · https://doi.org/10.1371/journal.pone.0311416
- TotalSegmentator: https://github.com/wasserth/TotalSegmentator
- XCAT (secondary): https://cvit.duke.edu/resource/xcat-brain-phantom/

Airway models
- Montesantos et al. 2016 (open access): https://doi.org/10.1371/journal.pone.0168026
- Kitaoka, Takaki, Suki 1999: https://doi.org/10.1152/jappl.1999.87.6.2207

4D rendering
- Engine4D (MIT): https://github.com/HackerPoet/Engine4D · https://raw.githubusercontent.com/HackerPoet/Engine4D/main/Assets/ShadersND/Core4D.cginc
- Zucconi, Rendering 4D objects: https://www.alanzucconi.com/2023/07/06/rendering-4d-objects/

Education (secondary)
- VERT evaluation for medical students: https://livrepository.liverpool.ac.uk/3182501/

Own computations (`research/R2_assets/`)
- `rt_probe.py`, `rt_motion.py`, `rt_lung.py`: TCIA queries and the per-patient table; `4dlung_motion_table.json`: results
- `slicer_proto.py`: tetrahedron slicer, tesseract and moving-sphere tests
