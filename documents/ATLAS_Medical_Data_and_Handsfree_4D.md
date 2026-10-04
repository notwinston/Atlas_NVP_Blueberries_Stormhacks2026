# Fold Heartbeats, Eyes and Gait into ATLAS

ATLAS can add eight new medical blades this weekend using data with permissive licences that can be downloaded quickly.
The best four are a Heartbeat Torus, a Visual Field Dome, a Tumour Growth Corridor and a Gait Torus.
The Heartbeat Torus uses Sunnybrook cine MRI (magnetic resonance imaging), which is public domain, plus PTB-XL electrocardiograms.
The Visual Field Dome uses UWHVF (University of Washington Humphrey Visual Fields), released under the BSD-3 licence.
The Tumour Growth Corridor uses LUMIERE glioblastoma follow-up scans under CC BY 4.0 (Creative Commons Attribution).
The Gait Torus uses CMU (Carnegie Mellon University) motion capture plus GaitRec.
Each one maps cleanly onto an existing ATLAS rule. Periodic data goes on a circle or torus, directions go on a sphere, and time becomes the 4th axis.
For input, three channels replace the controller. Walking is the slider for w or phase. Head-gaze is the pointer. A tap on the headset shell is the click.
Head roll works as a fine dial, and the Slice Cube handles 4D rotation.
Dwell selection is the fallback when a tap fails. A crouch-and-hold, plus automatic triggers, gives a safety exit with no buttons.
The weak points are 4D-learning evidence (small studies, no significant effects), mobile volume rendering (no measured iPhone stereo numbers exist) and gesture false positives while walking (nobody has measured them).
Run the spikes in the last section before you commit to any of these.

## Eight blades you can build from permissive data this weekend

The ranking weighs licence safety, download friction and how literally the data fits the geometry.
"Ship" means you can bake derived meshes into the app build.

| Rank | Blade | Dataset(s) | Licence | Download | Geometry | Ship? |
|---|---|---|---|---|---|---|
| 1 | Heartbeat Torus | Sunnybrook + PTB-XL | CC0 + CC BY 4.0 | ~2.7 GB + 1.7 GB, direct | T² (cardiac S¹ x breathing S¹) | Yes |
| 2 | Visual Field Dome | UWHVF | BSD-3 | One JSON file | S² + time | Yes |
| 3 | Tumour Growth Corridor | LUMIERE | CC BY 4.0 | Figshare, size not stated | 3D + time axis | Yes |
| 4 | Gait Torus | CMU mocap + GaitRec | Permissive + CC BY 4.0 | CMU >4 GB, GaitRec CSV | T² (hip x knee) | Yes |
| 5 | Circle of Willis in H² | TopCoW 2024 | Mixed per file | 14.4 GB Zenodo, open | H² | Yes for CC0 subset |
| 6 | Retina Tree in H² | FIVES + HRF | CC BY 4.0 | Small image sets | H² | Yes |
| 7 | Heart Shape Slider | 1,000 synthetic hearts | CC BY 4.0 | 25-mesh ZIPs | Shape space as 4th axis | Yes |
| 8 | Colon Cylinder | TCIA CT Colonography | CC BY 3.0 | Pull a few series only | Cylinder (S¹ x line) | Yes |

Glossary for the table.
S¹ is a circle. S² is a sphere. T² is a torus. H² is the hyperbolic plane.
CC0 is the Creative Commons public domain dedication.
TCIA is The Cancer Imaging Archive.

### 1. Heartbeat Torus (Sunnybrook CC0 + PTB-XL)

Sunnybrook has 45 cine MRI patients with manual LV (left ventricle) contours and LV models, in five DICOM batches of about 2.7 GB, all CC0 ([Cardiac Atlas Project](https://www.cardiacatlas.org/sunnybrook-cardiac-data/)).

PTB-XL has **21,799 12-lead ECGs (electrocardiograms)**, each 10 s long at 500 Hz, under CC BY 4.0 in a 1.7 GB ZIP ([PhysioNet](https://physionet.org/content/ptb-xl/1.0.3/)).

R-peaks give a phase angle directly. Phase = (t − R_k) / (R_{k+1} − R_k), mapped to a full turn.

Cross that cardiac circle with the breathing circle you already have from TCIA 4D-Lung. The result is a torus.

No public imaging dataset with both cardiac and respiratory bins was found, so the torus is composed from two sources. Say that plainly to judges.

### 2. Visual Field Dome (UWHVF, BSD-3)

UWHVF holds **28,943 Humphrey visual field tests** from 3,871 patients and 7,428 eyes, collected 1998 to 2018, in one JSON file under BSD-3 ([GitHub uwhvf](https://github.com/uw-biomedical-ml/uwhvf)).

Each test point is a viewing direction. Wrap the points around the user's head as a dome.

Repeat tests per eye give a time axis. Walk forward to watch glaucoma eat the dome over years.

This is the only blade where the input space and the data space are the same manifold. Where you look *is* the coordinate.

### 3. Tumour Growth Corridor (LUMIERE, CC BY 4.0)

LUMIERE has **91 glioblastoma patients, 638 study dates and 2,487 NIfTI images**, with expert RANO (Response Assessment in Neuro-Oncology) ratings and automated tumour segmentations for 599 dates ([Suter et al., Sci Data 2022](https://pmc.ncbi.nlm.nih.gov/articles/PMC9755255)).

Data sits on [Figshare](https://figshare.com/collections/The_LUMIERE_Dataset_Longitudinal_Glioblastoma_MRI_with_Expert_RANO_Evaluation/5904905).

Run marching cubes on each timepoint's label map. Lay the meshes along the corridor. Walking scrubs months.

The Slice Cube can cut the brain at each date.

### 4. Gait Torus (CMU mocap + GaitRec)

CMU mocap "may be copied, modified, or redistributed without permission", and commercial use is allowed ([CMU FAQ](http://mocap.cs.cmu.edu/faqs.php)).

Hip flexion against knee flexion over one stride traces a closed loop on a torus.

GaitRec adds **2,295 subjects and 75,732 walking trials** of ground reaction forces, time-normalised to 101 points of stance, under CC BY 4.0 ([GaitRec, PMC7217853](https://pmc.ncbi.nlm.nih.gov/articles/PMC7217853)).

Those curves are already phase-parameterised. Healthy and impaired gait become different loops on the same circle.

Patients had several sessions during rehab, so a real longitudinal axis comes free.

Avoid AMASS here. Its licence bans copying and redistribution ([AMASS licence](https://amass.is.tue.mpg.de/license.html)).

### 5. Circle of Willis in H² (TopCoW 2024)

TopCoW has **125 training patients with paired CTA and MRA**, 13 vessel labels, and an edge-list YAML for 8 Circle of Willis edges ([TopCoW data](https://topcow24.grand-challenge.org/data/)).
CTA is CT (computed tomography) angiography. MRA is MR angiography.

The Zenodo record is 14.4 GB and open, but licences differ per file ([Zenodo 15692630](https://zenodo.org/records/15692630)).
The TopCoW training labels require attribution and permission for commercial use.
MRA_Lausanne is CC0. MRA_IXI_HH is CC BY-SA 3.0. CTA_ISLES2024_TUM is CC BY-NC.
Use the Lausanne MRA cases for anything you ship.

Design note. The Circle of Willis core is a loop, not a tree. Put the ring at the disk centre and let the branching arteries fan out into H².

You will need to compute centrelines yourself, by skeletonising the masks.

### 6. Retina Tree in H² (FIVES + HRF)

FIVES has **800 fundus photos at 2048x2048** with manual vessel masks, split across normal, AMD, diabetic retinopathy and glaucoma, under CC BY 4.0 ([FIVES, Sci Data 2022](https://pmc.ncbi.nlm.nih.gov/articles/PMC9352679)).

HRF has 45 images with vessel masks plus optic disc centre and radius, under CC BY 4.0 ([FAU HRF](https://www5.cs.fau.de/research/data/fundus-images/)).

Root the tree at the optic disc. Skeletonise in 2D. Lay out in the Poincare disk.

This is the cheapest H² blade by far. It pairs with the Visual Field Dome as an "eye wing".

### 7. Heart Shape Slider (1,000 synthetic hearts, CC BY 4.0)

The King's College virtual cohort has **1,000 four-chamber heart meshes** plus PCA (principal component analysis) modes, 27.5 GB in 40 ZIPs of 25 meshes, under CC BY 4.0 ([Zenodo 4506930](https://zenodo.org/records/4506930)).

Sliding along PCA mode 1 or 2 is a 4th axis that is not time. It is shape space.

Download a single 25-mesh ZIP. The meshes are tetrahedral, so extract and decimate surfaces to fit the triangle budget.

The smaller "extreme and average" record may be faster, but its licence was not checked ([Zenodo 4593739](https://zenodo.org/records/4593739)).

### 8. Colon Cylinder (TCIA CT Colonography, CC BY 3.0)

The collection has **825 cases with prone and supine scans** under CC BY 3.0 ([TCIA](https://www.cancerimagingarchive.net/collection/ct-colonography/)).

It totals 496.67 GB and needs the TCIA Data Retriever. Pull two or three patients only.

The colon is inflated with air, so a threshold segments the lumen.

The centreline is a line and the cross-section angle is S¹. The wall unrolls into a strip whose edges glue into a cylinder.

Prone vs supine of the same patient gives a free deformation axis.

### Honourable mentions

MSD (Medical Segmentation Decathlon) Task08 has 443 CT volumes with hepatic vessel labels under CC BY-SA 4.0 ([medicaldecathlon.com](http://medicaldecathlon.com/)). It is a solid liver H² blade if you accept ShareAlike.

HuBMAP's CCF library ships ready GLB (binary glTF) whole-body organ meshes under CC BY 4.0 ([GitHub](https://github.com/hubmapconsortium/ccf-3d-reference-object-library)). It is a good "lobby" body with no segmentation work.

The USC 75-Speaker Speech MRI database has real-time vocal tract MRI with synced audio, and its raw data is public domain ([USC SAIL](https://sail.usc.edu/span/75speakers/)). Repeated syllables are a natural S¹ loop.

TractoInferno is 350 GB of CC BY 4.0 tractography, so pull one subject ([Poulin et al.](https://pmc.ncbi.nlm.nih.gov/articles/PMC9700736)).
Allard and Serrano found hyperbolic distances "exceptionally congruent" with connectome structure ([arXiv 1801.06079](https://arxiv.org/abs/1801.06079)).
A 2024 counterpoint argues Solv geometry competes ([arXiv 2407.16077](https://arxiv.org/pdf/2407.16077)). Keep it ready for judge questions.

## Full catalogue of 51 datasets beyond lungs

Ship column legend.
**Yes** means you can bake derived meshes into the build with attribution.
**Yes (SA)** means you can, but your derived meshes must carry the same ShareAlike licence.
**Demo only** means non-commercial use is fine for a student hackathon but blocks a product.
**Ask** means the licence was not verified.
**No** means the licence bans redistribution or derivatives.

| Dataset | Organ | 4D? | Licence | Ship meshes? | Geometry |
|---|---|---|---|---|---|
| [Sunnybrook](https://www.cardiacatlas.org/sunnybrook-cardiac-data/) | Heart (LV) | Yes, cine | CC0 | Yes | S¹ phase, time axis |
| [ACDC](https://huggingface.co/datasets/mathpluscode/ACDC/blob/main/README.md) | Heart (LV, RV, myocardium) | ED/ES labels | CC BY-NC-SA 4.0 (HF mirror) | Demo only | Time axis, bullseye on S² |
| [CAMUS](https://www.creatis.insa-lyon.fr/Challenge/camus/) | Heart, 2D echo | 2D + time | Not verified | Ask | S¹ |
| [CETUS](https://humanheart-project.creatis.insa-lyon.fr/databases.html) | Heart, 3D echo | 3D + time, one cycle | Not stated | Ask | Time axis |
| [TED](https://humanheart-project.creatis.insa-lyon.fr/databases.html) | Heart, 2D echo | 2D + time | Not stated | Ask | S¹ |
| [MIMI](https://humanheart-project.creatis.insa-lyon.fr/databases.html) | Heart (infarct) | Cine included | Collaboration agreement | No | None this weekend |
| [EchoNet-Dynamic](https://echonet.github.io/dynamic/) | Heart, echo video | Yes | Stanford research agreement, no derivatives | No | Private phase signal only |
| [Arshad free-running CMR](https://zenodo.org/records/12515230) | Heart, 4D flow | 20 cardiac phases | CC BY 4.0 | Yes | S² flow directions, S¹ |
| [1,000 synthetic hearts](https://zenodo.org/records/4506930) | Heart, 4 chambers | Shape space | CC BY 4.0 | Yes | PCA mode as 4th axis |
| [PTB-XL](https://physionet.org/content/ptb-xl/1.0.3/) | ECG | 10 s signals | CC BY 4.0 | Yes | S¹ driver |
| [TopCoW 2024](https://zenodo.org/records/15692630) | Brain arteries | No | Mixed per file | Yes for CC0 subset | H² |
| [IXI](https://brain-development.org/ixi-dataset/) | Brain (MRA, DTI, T1) | No | CC BY-SA 3.0 | Yes (SA) | H² after segmentation |
| [MSD, all tasks](http://medicaldecathlon.com/) | Liver vessels, heart, pancreas, colon and more | No | CC BY-SA 4.0 | Yes (SA) | H² (hepatic tree) |
| [3D-IRCADb-01](https://www.ircad.fr/research/data-sets/liver-segmentation-3d-ircadb-01/) | Liver + vessels | No | CC BY-NC-ND 4.0 | No | H² |
| [ImageCAS](https://github.com/XiaoweiXu/ImageCAS-A-Large-Scale-Dataset-and-Benchmark-for-Coronary-Artery-Segmentation-based-on-CT) | Coronary arteries | No | None stated, emailed password | Ask | H² |
| [ASOCA](https://asoca.grand-challenge.org/) | Coronary arteries | No | Not shown, UK Data Service | Ask | H² |
| [Vascular Model Repository](https://www.vascularmodel.com/FAQs.html) | Vessels | No | Research and development only | Demo only | H², flow |
| [TotalSegmentator v2.0.1](https://zenodo.org/records/10047292) | Whole body, 117 classes | No | CC BY 4.0 | Yes | Anchor scene, aorta tree |
| [AMOS](https://zenodo.org/records/7262581) | Abdomen, 15 organs | No | CC BY 4.0 | Yes | Anchor scene |
| [AbdomenAtlas](https://github.com/MrGiovanni/AbdomenAtlas) | Abdomen | No | Not confirmed | Ask | Anchor scene |
| [HuBMAP CCF](https://github.com/hubmapconsortium/ccf-3d-reference-object-library) | Whole body GLB | No | CC BY 4.0 | Yes | Lobby body |
| [Visible Human](https://www.nlm.nih.gov/research/visible/visible_human.html) | Whole body slices | No | No licence agreement since 2019 | Ask | Slice axis |
| [BodyParts3D](https://lifesciencedb.jp/bp3d/info/license/index.html) | Whole body meshes | No | CC BY-SA 2.1 JP | Yes (SA) | Anchor meshes |
| [Z-Anatomy](https://github.com/Z-Anatomy/Models-of-human-anatomy) | Whole body meshes | No | CC BY-SA 4.0 | Yes (SA) | Anchor meshes |
| [KiTS23](https://github.com/neheller/kits23) | Kidney | No | CC BY-NC-SA | Demo only | Organ mesh |
| [CT Colonography](https://www.cancerimagingarchive.net/collection/ct-colonography/) | Colon | Prone/supine pair | CC BY 3.0 | Yes | Cylinder |
| [USC 75-Speaker](https://sail.usc.edu/span/75speakers/) | Vocal tract | 2D real-time MRI + audio | Raw data public domain | Yes | S¹ or T² |
| [CMU mocap](http://mocap.cs.cmu.edu/faqs.php) | Whole-body motion | Yes | Free reuse, commercial OK | Yes | T² hip x knee |
| [AMASS](https://amass.is.tue.mpg.de/license.html) | Whole-body motion | Yes | Non-commercial, no redistribution | No | T² |
| [GaitRec](https://pmc.ncbi.nlm.nih.gov/articles/PMC7217853) | Gait forces | Yes | CC BY 4.0 | Yes | S¹ + sessions |
| [PhysioNet gaitdb](https://physionet.org/content/gaitdb/1.0.0/) | Stride intervals | Yes | ODC Attribution 1.0 | Yes | S¹ drift |
| [VerSe](https://github.com/anjany/verse) | Spine | No | CC BY-SA 4.0 | Yes (SA) | Chain, S² vertebra axes |
| [OAI](https://fnih.org/?p=6207) | Knee | Longitudinal 2002 to 2015 | Access not verified | Ask | Time axis |
| [NV-Generate-CT (MAISI)](https://huggingface.co/nvidia/NV-Generate-CT) | Synthetic CT | No | NVIDIA Open Model Licence, needs 80 GB GPU | Ask | Skip |
| [LUMIERE](https://pmc.ncbi.nlm.nih.gov/articles/PMC9755255) | Brain tumour | Longitudinal | CC BY 4.0 | Yes | Time axis |
| [TractoInferno](https://pmc.ncbi.nlm.nih.gov/articles/PMC9700736) | White matter | No | CC BY 4.0 | Yes | H² connectome |
| [OpenNeuro](https://openneuro.readthedocs.io/faq.html) | Brain, fMRI | 3D + time | CC0 | Yes | S² cortex + time |
| [BigBrain surfaces](https://ftp.bigbrainproject.org/bigbrain-ftp/Welcome.txt) | Cortex | No | Research only | Demo only | S² |
| [fsaverage](https://surfer.nmr.mgh.harvard.edu/fswiki/License) | Cortex | No | FreeSurfer licence not read | Ask | S² |
| [HCP Young Adult](https://humanconnectome.org/study/hcp-young-adult/data-use-terms) | Brain | fMRI | Every recipient must register | No | S² |
| [Allen / MICrONS](https://alleninstitute.org/terms-of-use/) | Neurons | MICrONS has calcium imaging | Non-commercial | Demo only | H² |
| [NeuroMorpho.Org](https://neuromorphological-file-specification.readthedocs.io/en/4.0.0/Legal.html) | Neurons | No | Not confirmed | Ask | H² |
| [UWHVF](https://github.com/uw-biomedical-ml/uwhvf) | Eye, visual field | Longitudinal | BSD-3 | Yes | S² + time |
| [FIVES](https://pmc.ncbi.nlm.nih.gov/articles/PMC9352679) | Retina | No | CC BY 4.0 | Yes | H² |
| [HRF](https://www5.cs.fau.de/research/data/fundus-images/) | Retina | No | CC BY 4.0 | Yes | H² |
| [FeTA v2](https://zenodo.org/record/4541606) | Fetal brain | Cross-sectional age | Research and education, gated | No | Age axis |
| [Amsterdam Embryo Atlas](https://anatomytool.org/node/59717) | Embryo | Carnegie stages 7 to 23 | CC BY-NC-ND | No | Stage axis |
| [DMDD](https://www.sanger.ac.uk/science/tools/dmdd/dmdd/) | Mouse embryo | No | "Freely available", no licence | Ask | Stage axis |
| [AlphaFold DB](https://alphafold.ebi.ac.uk/assets/License-Disclaimer.pdf) | Proteins | No | CC BY 4.0, commercial OK | Yes | Molecule mesh |
| [mdCATH](https://www.huggingface.co/datasets/compsciencelab/mdCATH) | Proteins | MD trajectories | CC BY 4.0 | Yes | Time axis |
| [Allen Cell Explorer](https://www.allencell.org/terms-of-use.html) | Cells | No | Non-commercial | Demo only | Cell mesh |

Abbreviations in the table.
LV and RV are left and right ventricle. ED and ES are end-diastole and end-systole.
DTI is diffusion tensor imaging. fMRI is functional MRI. MD is molecular dynamics. HF is Hugging Face.

## Licence traps that would block a product later

ATLAS is a student hackathon today. You want it to become a product. NC licences are fine for the first and fatal for the second.

**NC (NonCommercial)** blocks selling. This covers ACDC, KiTS23, BigBrain, Allen, MICrONS and the TopCoW ISLES CTA files.

**ND (NoDerivatives)** blocks decimating or re-meshing. That is exactly what you would do to 3D-IRCADb's ready VTK meshes and the Amsterdam embryo atlas.

**SA (ShareAlike)** is safe but sticky. Meshes from MSD, VerSe, BodyParts3D, Z-Anatomy or IXI must ship under the same licence.

**No-redistribution** clauses kill bundling outright. That means EchoNet-Dynamic, AMASS and HCP, where every recipient must register ([HCP terms](https://humanconnectome.org/study/hcp-young-adult/data-use-terms)).

**Mixed records** are the subtle trap. TopCoW has four licences inside one Zenodo record.

The clean core is CC0, CC BY, BSD and CMU. Build the shipped app from those. Keep everything else behind a "research demo" flag.

## Walking, head roll and a headset tap replace the controller

Three findings drive the input design.

Head-gaze pointing reached **2.47 bits/s throughput**, against 2.13 for eye-gaze and 3.24 for a mouse, with an effective target width of about 2.5 degrees ([Hansen, MacKenzie et al., ETRA 2018](https://www.yorku.ca/mack/etra2018.html)).

Taps and scratches on a Cardboard shell were recognised from the phone's own microphones at **97.7 to 98.2 percent** across 33 gestures ([GestOnHMD](https://www.scm.cityu.edu.hk/research/projects/gestonhmd)).

iOS Back Tap is reserved for system commands, so you must build tap detection yourself ([Apple forums](https://developer.apple.com/forums/thread/699206)).

### Input map

Confidence reflects both the evidence and how well it transfers to a phone in a viewer while walking.

| Physical action | ATLAS operation | Evidence | Confidence |
|---|---|---|---|
| Walk along an axis or around a ring | w offset, or breathing/cardiac phase | 4D Toys makes a continuous 1D w slider its core input ([Road to VR](https://roadtovr.com/hands-4d-toys-mind-bending-unique-vr-puzzler/)). Seated space-time cube scored SUS 82.3 vs 62.1 on desktop ([arXiv 1908.00580](https://ar5iv.arxiv.org/html/1908.00580)). No walk-to-scrub study exists. | High to detect, Medium to understand |
| Head roll, kept within ±30° | Continuous dial for fine phase, slice offset or H² pan | Roll left/right was among the lowest-error, best-rated head mode switches, all under 850 ms ([arXiv 2108.05538](https://arxiv.org/pdf/2108.05538)). No dial accuracy study. | Medium |
| Crouch and hold | Safety pause to camera passthrough | Spatial threshold on ARKit eye height. No study. | High to detect |
| Step or torso lean | Zoom, or step one phase | Move-backward had the lowest end error at 10.34 percent ([arXiv 2108.05538](https://arxiv.org/pdf/2108.05538)). Neck-only lean was dropped for discomfort ([arXiv 2209.06825](https://ar5iv.labs.arxiv.org/html/2209.06825)), so use the whole torso. | Medium |
| Head-gaze + dwell | Select (fallback) | Dwell had the fewest errors but the slowest throughput ([SFU, Pinch Click Dwell](https://vvise.iat.sfu.ca/user/data/papers/pinchclickdwell.pdf)). A 1 s dwell took 2.88 s per selection with 7.0 percent errors ([arXiv 2209.06825](https://ar5iv.labs.arxiv.org/html/2209.06825)). | High |
| Tap headset shell | Click (A) | 98.2 percent detection on Cardboard ([GestOnHMD](https://www.scm.cityu.edu.hk/research/projects/gestonhmd)). No data while walking. | Medium to High |
| Nod, gated to a target | Confirm | 98.5 percent accuracy, 0.71 s to recognise, confused with fast head turns ([arXiv 1707.06691](https://arxiv.org/pdf/1707.06691)) | Medium standing, Low walking |
| Rear-camera hand pinch | Confirm, or grab the 4D object | Gaze + pinch matched button-click throughput ([SFU](https://vvise.iat.sfu.ca/user/data/papers/pinchclickdwell.pdf)). Vision hand pose exists since iOS 14 ([Apple](https://developer.apple.com/documentation/vision/vndetecthumanhandposerequest.md)). Hands must be raised into camera view, a high-fatigue posture ([Meta](https://developers.meta.com/horizon/design/hands-best-practices/)). | Medium, fps untested |
| Slice Cube in hand | SO(4) rotation from the marker quaternion | Reuses your ARKit image tracking. A pair of quaternions parametrises all 4D rotations. | Medium |
| Head quaternion as S³ position | "Hyper-look" mode through a 4D polytope | Hypernom does exactly this and runs on a phone. Full coverage needs every head orientation twice, including upside down ([arXiv 1507.05707](https://ar5iv.labs.arxiv.org/html/1507.05707)). | Medium, comfort Low |
| Voice keywords | Backup commands | Humming was slowest at 3.41 s with 11.4 percent errors, and 16 of 24 users ranked it last as "embarrassing" ([arXiv 2209.06825](https://ar5iv.labs.arxiv.org/html/2209.06825)). iOS 26 SpeechAnalyzer runs offline ([guide](https://antongubarenko.substack.com/p/ios-26-speechanalyzer-guide)). | Low as primary |
| Blow into mic | Binary trigger | BlowClick did "better than expected" against a Flystick, no numbers ([RWTH](https://graphics.rwth-aachen.de/publication/028)) | Low (hygiene for shared demos) |
| TrueDepth jaw open or blink | Nothing until tested | ARKit supports face and world tracking together ([Apple](https://developer.apple.com/documentation/arkit/combining-user-face-tracking-and-world-tracking.md)). No documented minimum distance. | Unknown |

SUS is the System Usability Scale. SO(4) is the group of rotations in 4D space. S³ is the 3-sphere.

### Breath Room should become a ring

Walking east makes a line, and a breath is a loop. The line has a seam at end-of-exhale.

Map phase to the angle around the room centre instead. One lap equals one breath.

A 1.2 m radius ring fits the 3x3 m area and gives about 7.5 m of walking per cycle.

The same ring drives the Heartbeat Torus. Laps give breathing. Head roll gives cardiac phase.

### Giving the six 4D rotation planes a body part each

4D rotation has six planes. xy, yz and xz behave like normal 3D rotation and come from head pose or the Slice Cube.

The three w-planes (xw, yw, zw) are the hard ones. The Slice Cube's full quaternion is the cleanest source.

Hanson's rolling ball turns any 3-vector into a w-plane rotation ([Hanson notes](https://homes.luddy.indiana.edu/hansona/4DTouchSidebars/4droll.pdf)). Feed it the head-gaze offset from a reference direction.

Keep w on its own unmistakable channel. In Igarashi and Sawada's maze study, users first tried 3D operations, and mastering the 4th direction decided completion time ([EG 2023](https://diglib7.eg.org/bitstream/handle/10.2312/egve20231319/115-119.pdf)).

Your Look-Down Shadow matches 4D Golf's "ghost image" projection of off-slice objects ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/FourDGolf)). It is an accepted idiom, not a gamble.

## Six gestures cover everything judges need

This set replaces gamepad A and B with no hardware.

| Need | Primary | Fallback |
|---|---|---|
| Point | Head-gaze reticle | None needed |
| A (select) | Single tap on headset shell, accepted only while the reticle is on a target | Dwell 800 ms with a visible fill ring |
| B (back) | Double tap on the shell | Look up and cross the "back" gate overhead |
| Scrub w or phase | Walk the line or ring | Head roll dial |
| Rotate in 4D | Turn the Slice Cube | Hypernom mode for head-only |
| Look at the projection | Look down (existing Look-Down Shadow) | None needed |

Why 800 ms dwell. Studies used 300 ms for experienced eye-tracking users. No study measured head-gaze dwell while walking. Treat 300 ms as the floor and start novices near 800 ms.

Why crossing for B. Crossing was as fast or faster than pointing with equal or better accuracy in VR headsets ([Tu et al. 2019](https://paper.nweon.com/?p=1961)). It also avoids the dwell wait.

Looking up is free because Look-Down Shadow owns the downward gaze.

Skip head shake for B. It gets confused with fast head turns ([arXiv 1707.06691](https://arxiv.org/pdf/1707.06691)).

### Safety exit with no buttons

Any one of four triggers fades the scene to camera passthrough.

The first is automatic. It fires when the user comes within 0.5 m of the 3x3 m boundary, using ARKit position.

The second is also automatic. It fires when ARKit tracking state drops from normal.

The third is deliberate. Crouch below about 70 percent of calibrated standing eye height and hold for 1.5 s.

The fourth is a voice "stop", if voice is enabled.

Tell every judge one sentence before they put it on. "If anything feels off, crouch."

Crouch works when the user is disoriented, needs no aim and is hard to trigger by accident.

## Five risks to retire before the weekend

### Mobile volume rendering has no published budget

No hard numbers exist for stereo ray-marched volumes on an iPhone.

A 2025 desktop study hit 136 to 739 fps on 512-voxel-class volumes with an RTX 4080. Its authors still said standalone headsets "are not powerful enough" ([PMC12575470](https://pmc.ncbi.nlm.nih.gov/articles/PMC12575470/)).

Ten breathing phases at 256³ in 8-bit take about 168 MB. At 128³ they take about 21 MB.

Fallback order is meshes per phase first, then a slice plane, then a half-resolution 128³ ray march.

A slice plane costs one 3D-texture lookup per pixel. It also fits the 4D-slice story.

Blending two adjacent phases per frame is a published 4D CT technique ([Fortmeier 2015](https://biblio.imi.uni-luebeck.de/pdf/2015TOH_Fortmeier.pdf)).

### Licence traps

See the licence section. The fix is a per-file audit before anything enters the build.

### TrueDepth inside the headset is untested

ARKit can run face tracking with world tracking at the same time ([Apple](https://developer.apple.com/documentation/arkit/combining-user-face-tracking-and-world-tracking.md)).

But the front camera sits behind the viewer's divider, a few centimetres from the face. It was designed for arm's length.

It also adds compute load. Do not plan any feature on it until a 10-minute test passes.

### Gestures will misfire while walking

No study reports a false-positive rate for head gestures or shell taps during locomotion.

Users in the mode-switching study asked for better false-positive resistance while moving ([arXiv 2108.05538](https://arxiv.org/pdf/2108.05538)).

Footsteps can spike the accelerometer like a tap.

Mitigation is to fuse an accelerometer spike with a mic transient inside about 50 ms, and to accept input only while the reticle is on a target.

### The evidence for learning 4D is thin

Igarashi and Sawada tested 10 students. Times ranged from 182 to 3,524 s, with no significant group difference ([EG 2023](https://diglib7.eg.org/bitstream/handle/10.2312/egve20231319/115-119.pdf)).

Collins and Regenbrecht tested 22. The biggest self-reported gain came from the instructional video, not the hands-on hypercube ([ISMAR 2018](https://www.hci.otago.ac.nz/papers/CollinsIEEEISMAR2018.pdf)).

Ambinder et al. called 4D representations "primitive and short lived" ([Springer](https://link.springer.com/article/10.3758%2FPBR.16.5.818)).

The medical side is stronger. A meta-analysis of 27 studies and 2,199 students found XR beat traditional anatomy teaching at SMD 0.40, and beat lectures at 1.00 ([García-Robles et al. 2024](https://ruja.ujaen.es/items/5f98f7dc-bfea-4970-a25b-86fb558f9d25)).
XR is extended reality. SMD is standardised mean difference.

Pitch ATLAS as embodied exploration of medical data. Do not claim it teaches 4D.

### Smaller risks

Vision body pose took about 40 ms per frame on an iPhone 11 ([Apple forums](https://developer.apple.com/forums/thread/679909)). Hand pose next to ARKit and stereo rendering will compete for compute.

A strapless viewer ties up one hand, which shrinks any hand gesture set to one hand.

Avoid magnet buttons. They disturb the compass ([ExtensionClip](https://www.iplab.cs.tsukuba.ac.jp/~isomoto/papers/takada_chilbw2018.pdf)).

## Eight spikes, in run order

| Spike | Time | Pass if |
|---|---|---|
| TrueDepth in viewer. Log `ARFaceAnchor` presence and `jawOpen` with the phone in the headset | 10 min | Anchor present in most frames |
| Licence audit. Check each file you plan to ship, starting with TopCoW and Zenodo 4593739 | 30 min | Every shipped file is CC0, CC BY, BSD or CMU |
| Tap detector walk test. 2 min of walking, then 20 deliberate taps | 30 min | At least 18 of 20 taps caught, at most 1 false trigger while walking |
| Volume budget. 128³ x 10 phases, stereo, ARKit running | 2 h | Steady 60 fps, otherwise fall back to meshes |
| Dwell sweep. 500, 800 and 1,000 ms with 3 people | 30 min | Pick the fastest with no accidental selects |
| Head roll dial while walking | 30 min | Dial jitter small enough to land on one phase bin |
| Ring vs line Breath Room with 3 people | 1 h | People can say which phase they are in |
| Vision hand pose at 15 Hz and 30 Hz on a downscaled frame | 1 h | Under 5 fps drop, otherwise drop pinch |

These pass thresholds are proposals, not published standards. Adjust after the first run.

## Conclusion

The strongest blades are the ones where the body's own geometry matches the data's.
Head direction is a point on S², so the Visual Field Dome needs no mapping at all.
A walked ring is S¹, so breathing and heartbeat need no buttons.
Push the pitch toward those cases. They make "every space has the shape of what's inside it" true for the user's body as well as the data.

The licence filter is also a product filter.
The CC0, CC BY, BSD and CMU set covers heart, eye, brain tumour, gait, retina, colon and whole body.
That means ATLAS can ship as something sellable without rebuilding its content later.
The open questions are empirical, not legal. They are rendering budget, walking false positives and the TrueDepth camera, and each one takes an afternoon to answer.
