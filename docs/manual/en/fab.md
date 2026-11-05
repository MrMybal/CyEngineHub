# Epic / Fab account and owned assets

Open Settings → Connections → Epic or Fab. The direct Epic/Fab session supplies the owned library and native downloads. It is distinct from a website session used by a browser connector.

## Sign in

Use Sign in inside CyEngineHub to open the official Epic page. Complete login, MFA and account checks yourself. The Windows WebView2 Evergreen Runtime is required for embedded browsing; its loader is bundled, not the full browser runtime.

If the runtime is absent or SSO rejects embedded browsing, expand manual mode, open Epic in your external browser, and paste the requested official response/code into the connection field. Do not put it in notes, logs or screenshots. Cancel closes the pending login. A disabled connector and an expired session are different problems.

Passwords are not saved by the hub. Its direct session uses Windows secure storage; browser profiles are separate from your personal browser. No account checks, TLS warnings or vendor restrictions are bypassed. Reconnect when the provider expires/refuses the session.

## Download the intended variant

Refresh My Library, open a product, and inspect versions and platforms. Choose the exact Windows/source variant needed by the target, not an unrelated Mac variant with the same name. Native local reuse depends on matching asset/artifact identity and usable files, not just the title. If the expected local version is absent, inspect its recorded path and variant before allowing another transfer.

## Check asset updates

Refresh or use the downloaded-pack update check. The hub compares supplier distribution metadata/manifests for already downloaded native packs; checking alone does not download asset chunks or modify installed projects. A changed content fingerprint can indicate an update even if a supplier reused a build label. Missing baseline, removed artifacts or unavailable metadata must be shown as unverifiable, not current.

Download updated versions explicitly. They are stored alongside old versions; existing project installations and local derivatives are not silently replaced. See [downloads](help:downloads), [installation](help:install) and [troubleshooting](help:troubleshooting).
