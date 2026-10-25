# CyEngineHub licensing

Copyright (c) 2026 CyberAlien and CyEngineHub contributors.

## Hub: GPL-3.0-only

Except for the separately licensed components listed below and third-party
material, CyEngineHub's original source code, bundled extension scripts, build
scripts and documentation in this repository are licensed under the GNU General
Public License, version 3 only (`GPL-3.0-only`).

CyEngineHub is free software: you can redistribute it and/or modify it under the
terms of version 3 of the GNU General Public License as published by the Free
Software Foundation.

CyEngineHub is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the [full GPLv3 text](LICENSE) for details.

You should have received a copy of the GNU General Public License along with
CyEngineHub. If not, see <https://www.gnu.org/licenses/>.

## Separate Unreal components: MIT

The original CyEngineHub code in these two directories is **separately licensed
under MIT**, not GPL:

| Component | Source directory | License |
| --- | --- | --- |
| Unreal Editor client plugin | `extensions/unreal/CyEngineHub/` | [MIT](extensions/unreal/CyEngineHub/LICENSE) |
| Native asset conversion adapter | `extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/` | [MIT](extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/LICENSE) |

Each directory includes its own `LICENSE` and `NOTICE.md`. Their original code
remains MIT when built, packaged or installed separately. The native DLLs built
from this code carry the same notices. The surrounding Hub conversion service,
Python scripts, MCP service and other built-in extensions remain GPL-3.0-only,
unless an existing third-party notice says otherwise.

The editor client communicates with the Hub through its local IPC protocol;
the native conversion adapter is loaded by Unreal in temporary conversion projects.
Do not copy GPL Hub implementation code into the MIT plugins and assume that it
becomes MIT. A different integration must be reviewed on its own terms.

## Third-party material and engine content

Existing third-party copyrights, licenses and notices are preserved. This policy
does not relicense dependencies, vendor wheels, SDKs, fonts, logos owned by third
parties, Unreal Engine code or binaries, or marketplace assets and user projects.
See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), `LICENCES/`, package metadata
and the `Licenses/` directory in Windows distributions.

In particular, the MIT licenses above grant rights to our original plugin code,
not to Epic's engine code. Building or distributing plugins against Unreal may
also be subject to Epic's applicable terms. The Microsoft WebView2 SDK and
runtime retain Microsoft's terms. These notices are not a determination that
every possible combination of third-party binaries is GPL-compatible; verify
the applicable distribution terms before publishing a binary combination.

## Distributing builds and corresponding source

Keep this file, the full `LICENSE`, third-party notices and each plugin's MIT
notices in distributed packages. When distributing GPL-covered binaries, provide
the complete Corresponding Source for that exact version, including required
build/install scripts, in a manner allowed by GPLv3 section 6. A link to the
moving default branch alone is not a substitute for version-matching source.

Project source: <https://github.com/MrMybal/CyEngineHub>.

This policy applies to the source revision containing it and builds made from
that revision. It does not replace files in previously published release assets.
