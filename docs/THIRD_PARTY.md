# Third-party notices

No v1 source code or artwork was copied. Its mechanism audit is documented in
REUSE_LEDGER; no v1 license is imposed on new original code by this project.
No open-source license was selected for original private project code.

Dependencies retain their upstream terms and notices in installed packages:
ASP.NET/.NET, Microsoft.Data.Sqlite, SQLitePCLRaw, React, Vite, TypeScript,
react-markdown and its unified/micromark dependencies, lucide-react, xUnit,
Microsoft.AspNetCore.Mvc.Testing, ModelContextProtocol.AspNetCore and Playwright. See committed NuGet/npm lockfiles
for exact versions and their package distributions for license texts.
If redistributing a packaged build, generate a full transitive notice bundle first.
The raven and T mark are original temporary SVG artwork authored for this prototype.

The experimental worker recipe also includes OpenClaw and Docker's sandbox base
image. Pinned manifests and upstream locations are in `workers/openclaw/versions.json`.
Their distributed images retain their own dependency notices. Docker Sandboxes
is installed separately under Docker's terms; it is not part of the original
Thaddeus source. A production image/package still requires a full notice bundle.
