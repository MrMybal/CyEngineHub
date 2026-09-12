# Optional Megascans catalog search

CyEngineHub no longer distributes an Algolia key from Quixel Bridge. Being present
in another application's client files does not establish permission to reuse it.

Catalog search for assets and collections is disabled by default. If you have a
key and explicit permission to use it for the Quixel catalog, set the local
`CYENGINEHUB_QUIXEL_ALGOLIA_KEY` environment variable before starting CyEngineHub.
No example key or fallback value is supplied. An unrelated Algolia application key
does not grant access to Quixel's indexes. Do not commit the value, include it in a
support log, or distribute it in a release package. The program does not extract
keys automatically from Bridge.

Without a configured key, catalog queries stop locally with an explanatory error.
Epic/Quixel account sessions and authentication remain separate; providing a
catalog key does not authenticate an account or grant asset ownership.
