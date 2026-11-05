# Install a pack into projects

Installation copies/imports content into a target. Downloading only stores a library package. Conversion can prepare a different representation before installation. Select the correct operation rather than treating them as synonyms.

## Review the installation window

1. Open the product/local sheet and choose Install/configure installation.
2. Select one or more actual projects. Search and compatible-only filtering affect which targets are displayed; disable the filter to inspect others.
3. Inspect all offered variants, their engine versions and platform. A compatibility warning should inform your decision, not be mistaken for an unavailable download.
4. Choose the installation method/chain for each target and open Configure for its options. Confirm source engine, target engine, destination subfolder and conflict policy.
5. Prefer the exact usable local package when present. Review any network transfer and optional derivative creation before confirming Install into targets.

## Scripts and explicit force

Native content import, plugin installation, RAW import and version conversion are different methods. Chains can prepare data before a final installation; order and declared output types matter. Refresh scripts after adding a trusted local script.

The explicit force/bypass option allows attempting the selected method despite compatibility preflight warnings; it does not make unsupported serialization work, grant account access or supply missing files/tools. It does not guarantee success. Never use it as a reason to skip an independent backup or ignore the actual target shown.

Choose a conflict policy deliberately. Replacement backups and receipts apply to the workflows that support them; not every external script is transactional. Unreal live-editor imports use stricter additive behavior and will not replace existing assets. Close conflicting editor/build processes when a workflow needs exclusive access.

## Verify the result

Follow Actions through preparation, import and validation. Open the linked project, reload content in the target engine and inspect dependencies/materials/Blueprints. A transfer reaching 100% is not the final installation state. Retain reports for failed or approximated conversions. See [conversion](help:conversion), [plugins](help:plugins) and [derivatives](help:derivatives).
