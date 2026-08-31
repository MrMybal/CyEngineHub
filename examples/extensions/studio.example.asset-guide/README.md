# Studio asset guide example

This folder is a complete declarative CyEngineHub extension package. Replace the
example metadata and HTTPS URL, keep `extension.json` at the package root, then
select that folder from **Extensions Hub → Installer un package**.

The `$schema` property enables completion and validation in JSON-aware editors.
It is documentation-only at runtime. CyEngineHub still performs its own strict
manifest validation before presenting an installation plan.

`WebLink` packages can open an HTTPS resource but cannot execute a local binary
or write to a project. Declaring `ExternalProcess` does not grant execution
permission: user-installed executables remain blocked by the current trust
policy.
