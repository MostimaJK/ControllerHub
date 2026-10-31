# Third-party notices

ControllerHub integration sources: GPL-3.0-or-later (COPYING). Keep corresponding sources, dependency revisions and integration patches with redistribution.

- DS4Windows: https://github.com/hbashton/DS4Windows — GPL-3.0-or-later. Credits in upstream/DS4Windows/NOTICE.txt and contributors.txt. Integration changes: input-only facade, isolated profile directory, report hook, output suppression and virtual-input admission filter.
- PRO2 Bridge: https://github.com/LeonChrome/XinHeLianSheng-Pro2-Bridge — Apache-2.0 application sources. ViiperProtocolClient is linked into this build; packet layouts inform PadPackets and feedback conversion. License: licenses/PRO2-Apache-2.0.txt.
- VIIPER haptic backend — bundled unchanged, GPL-3.0. Source tree: upstream/PRO2/source/final-three-in-one/tools/viiper/haptic-src. Exact binary hash: DEPENDENCIES.lock.json. Corresponding-source release reference: https://github.com/LeonChrome/XinHeLianSheng-Pro2-Bridge/releases/download/pro2-v2.0/viiper-haptic-corresponding-source-v2.0.zip .
- usbip-win2 0.9.7.7 — BSD-2-Clause, unchanged installer. https://github.com/vadimgrn/usbip-win2/tree/v.0.9.7.7 . License: licenses/USBIP-BSD-2-Clause.txt. Installation is a separate elevated action.
- NS2ProCon artwork — ErenYaeger999, MIT. Preserve licenses/NS2ProCon-LICENSE.txt and docs/artwork.md.
- Transitive packages: see licenses/NUGET-PACKAGES.json for resolved versions and NuGet metadata links. Their license metadata and notices remain applicable.

This independent integration is not an official Nintendo, Sony or Microsoft application.

DualSense illustration: Al. Lopez / [Gamepad-Asset-Pack](https://github.com/AL2009man/Gamepad-Asset-Pack), MIT. See docs/artwork.md and licenses/Gamepad-Asset-Pack-LICENSE.txt for provenance and rights notes.

HidHide 1.5.230: https://github.com/nefarius/HidHide — MIT. Optional installer, unchanged; license: Backend/HidHide-LICENSE.txt. Installation is initiated only by the user; not required for offline checks.

Google Sans: Google Sans Project Authors / [google/fonts](https://github.com/google/fonts/tree/main/ofl/googlesans), SIL Open Font License 1.1. The application embeds static Regular and Bold instances generated from the pinned variable font for WPF compatibility; glyph outlines were not edited. License: licenses/GoogleSans-OFL.txt. Source revision and file hashes: DEPENDENCIES.lock.json.
